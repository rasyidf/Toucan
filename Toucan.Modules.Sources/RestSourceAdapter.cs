using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Plugins.Sources;

namespace Toucan.Modules.Sources;

/// <summary>
/// The Toucan source protocol (docs/source-protocol.md) as a unit adapter: ETag-style revisions, cursor pagination, idempotent
/// operations, a delta endpoint and one error format. Every request goes through the host's HTTP client, which attaches the
/// token, enforces the address and applies rate limits.
/// </summary>
public sealed class RestSourceAdapter : IContentSourceAdapter, ISupportsProjectDiscovery
{
    public const string AdapterId = "toucan.sources.rest";

    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = AdapterId,
        DisplayName = "Toucan source protocol",
        Icon = "Cloud",
        AllowsCustomBaseUrl = true,
        AuthMethods = [new ApiTokenAuth("token", "Access token")],
        Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.RenameKeys
            | SourceCapabilities.DeleteKeys | SourceCapabilities.DeleteTranslations | SourceCapabilities.ConditionalWrites | SourceCapabilities.DeltaReads
            | SourceCapabilities.ReviewStatus | SourceCapabilities.Comments | SourceCapabilities.ProjectDiscovery,
    };

    public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IContentSourceSession>(new Session(context));
    }

    public async IAsyncEnumerable<RemoteProjectSummary> ListProjectsAsync(ISourceAccountContext account, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        using var http = account.Http.Create();
        http.BaseAddress = Normalize(account.BaseUrl);
        var list = await SendAsync<ProjectList>(http, HttpMethod.Get, "v1/projects", null, cancellationToken).ConfigureAwait(false);
        foreach (var p in list?.Items ?? []) yield return new(p.Id, p.Name ?? p.Id, p.Description);
    }

    // ───────────── wire format ─────────────

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed record ProjectList(List<ProjectBrief>? Items);
    private sealed record ProjectBrief(string Id, string? Name, string? Description);
    private sealed record LanguageDto(string Code, string? Name, bool IsSource);
    private sealed record ResourceDto(string Id, string Name, string? Path);
    private sealed record PermissionsDto(bool WriteValues, bool CreateKeys, bool RenameKeys, bool DeleteKeys, bool DeleteTranslations, bool Status, bool Comments, bool ConditionalWrites, bool Changes);
    private sealed record LimitsDto(int? MaxBatchSize, int? MaxConcurrency, int? RequestsPerMinute);
    private sealed record ProjectDto(string Id, string Name, string? SourceLanguage, List<LanguageDto>? Languages, List<ResourceDto>? Resources, PermissionsDto? Permissions, LimitsDto? Limits);
    private sealed record KeyDto(string Resource, string Name, string? Id);
    private sealed record StatusDto(string? Value, string? Meaning);
    private sealed record UnitDto(KeyDto Key, string Language, string Value, string? Revision, StatusDto? Status, string? Comment);
    private sealed record UnitPageDto(List<UnitDto>? Items, string? NextCursor, bool Complete, string? DeltaCursor);
    private sealed record DeletedDto(KeyDto Key, string? Language);
    private sealed record ChangePageDto(List<UnitDto>? Changed, List<DeletedDto>? Deleted, string? NextCursor, bool Complete, string? Cursor, bool AuthoritativeDeletes);
    private sealed record ErrorDto(string? Code, string? Message, double? RetryAfterSeconds);
    private sealed record ErrorBody(ErrorDto? Error);
    private sealed record OperationDto
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public KeyDto? Key { get; init; }
        public string? Language { get; init; }
        public string? Value { get; init; }
        public string? Status { get; init; }
        public string? Comment { get; init; }
        public Dictionary<string, string>? Values { get; init; }
        public string? NewName { get; init; }
        public string? ExpectedRevision { get; init; }
    }
    private sealed record ResultDto(Guid Id, string? Outcome, string? Revision, string? RemoteId, UnitDto? Current, ErrorDto? Error);
    private sealed record ResultList(List<ResultDto>? Results);

    private static Uri Normalize(Uri baseUrl) => baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");

    private static async Task<T?> SendAsync<T>(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: s_json);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(s_json, ct).ConfigureAwait(false);
    }

    private static async Task<SourceException> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ErrorDto? error = null;
        try { error = (await response.Content.ReadFromJsonAsync<ErrorBody>(s_json, ct).ConfigureAwait(false))?.Error; }
        catch (JsonException) { }
        var kind = (int)response.StatusCode switch
        {
            401 => SourceErrorKind.Authentication,
            403 => SourceErrorKind.Authorization,
            404 => SourceErrorKind.NotFound,
            409 => SourceErrorKind.Conflict,
            408 or 504 => SourceErrorKind.Timeout,
            422 or 400 => SourceErrorKind.Validation,
            429 => SourceErrorKind.RateLimited,
            >= 500 => SourceErrorKind.Unavailable,
            _ => SourceErrorKind.Unknown,
        };
        var retry = response.Headers.RetryAfter?.Delta ?? (error?.RetryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
        return new SourceException(new SourceError(kind, error?.Message ?? $"The service answered {(int)response.StatusCode} {response.ReasonPhrase}.")
        {
            RetryAfter = retry, RemoteCode = error?.Code,
        });
    }

    private static SourceReviewMeaning Meaning(string? text) =>
        Enum.TryParse<SourceReviewMeaning>(text, ignoreCase: true, out var meaning) ? meaning : SourceReviewMeaning.Unknown;

    private static RemoteUnit ToUnit(UnitDto d) => new(new RemoteKey(d.Key.Resource, d.Key.Name, d.Key.Id), d.Language, d.Value)
    {
        Revision = d.Revision,
        Status = d.Status is { } s ? new RemoteReviewStatus(s.Value ?? s.Meaning ?? string.Empty, Meaning(s.Meaning)) : null,
        Comment = d.Comment,
    };

    private static string MeaningName(SourceReviewMeaning meaning) => char.ToLowerInvariant(meaning.ToString()[0]) + meaning.ToString()[1..];

    // ───────────── session ─────────────

    private sealed class Session : IUnitSourceSession, ISupportsDeltaReads
    {
        private readonly ISourceSessionContext _context;
        private readonly HttpClient _http;
        private readonly string _project;

        public Session(ISourceSessionContext context)
        {
            _context = context;
            _http = context.Http.Create();
            _http.BaseAddress = Normalize(context.BaseUrl);
            _project = Uri.EscapeDataString(context.Connection.RemoteProjectId);
        }

        public async Task<SourceTestResult> TestAsync(CancellationToken cancellationToken)
        {
            try
            {
                var project = await SendAsync<ProjectDto>(_http, HttpMethod.Get, $"v1/projects/{_project}", null, cancellationToken).ConfigureAwait(false);
                return SourceTestResult.Ok($"Connected to {project?.Name ?? _context.Connection.RemoteProjectId}.");
            }
            catch (SourceException ex)
            {
                return SourceTestResult.Failed(ex.Error);
            }
        }

        public async Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken)
        {
            var p = await SendAsync<ProjectDto>(_http, HttpMethod.Get, $"v1/projects/{_project}", null, cancellationToken).ConfigureAwait(false)
                ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "The service returned no project."));
            var caps = SourceCapabilities.ReadUnits | (p.Permissions is { Changes: true } ? SourceCapabilities.DeltaReads : 0);
            if (p.Permissions is { } x)
            {
                if (x.WriteValues) caps |= SourceCapabilities.WriteValues;
                if (x.CreateKeys) caps |= SourceCapabilities.CreateKeys;
                if (x.RenameKeys) caps |= SourceCapabilities.RenameKeys;
                if (x.DeleteKeys) caps |= SourceCapabilities.DeleteKeys;
                if (x.DeleteTranslations) caps |= SourceCapabilities.DeleteTranslations;
                if (x.Status) caps |= SourceCapabilities.ReviewStatus;
                if (x.Comments) caps |= SourceCapabilities.Comments;
                if (x.ConditionalWrites) caps |= SourceCapabilities.ConditionalWrites;
            }
            return new SourceProjectInfo(p.Id, p.Name, p.SourceLanguage,
                [.. (p.Languages ?? []).Select(l => new RemoteLanguage(l.Code, l.Name, l.IsSource))],
                [.. (p.Resources ?? []).Select(r => new RemoteResource(r.Id, r.Name, r.Path))], caps,
                p.Limits is { } l2 ? new SourceLimits(l2.MaxBatchSize, l2.MaxConcurrency, l2.RequestsPerMinute) : null);
        }

        public async IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var cursor = request.ContinuationToken;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new List<string>();
                foreach (var r in request.ResourceIds ?? []) query.Add("resource=" + Uri.EscapeDataString(r));
                foreach (var l in request.Languages ?? []) query.Add("language=" + Uri.EscapeDataString(l));
                if (cursor is not null) query.Add("cursor=" + Uri.EscapeDataString(cursor));
                var page = await SendAsync<UnitPageDto>(_http, HttpMethod.Get, $"v1/projects/{_project}/units" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty), null, cancellationToken).ConfigureAwait(false)
                    ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "The service returned an empty page."));
                cursor = page.NextCursor;
                // Only a page the server calls complete, with nothing after it, ends the read honestly.
                yield return new([.. (page.Items ?? []).Select(ToUnit)], cursor, page.Complete && cursor is null, page.DeltaCursor);
            }
            while (cursor is not null);
        }

        public async IAsyncEnumerable<RemoteChangePage> ReadChangesAsync(DeltaReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var cursor = request.Cursor;
            string? continuation = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = $"v1/projects/{_project}/changes?cursor={Uri.EscapeDataString(continuation ?? cursor)}";
                var page = await SendAsync<ChangePageDto>(_http, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false)
                    ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "The service returned an empty page."));
                continuation = page.NextCursor;
                yield return new([.. (page.Changed ?? []).Select(ToUnit)],
                    [.. (page.Deleted ?? []).Select(d => new RemoteDeletion(new RemoteKey(d.Key.Resource, d.Key.Name, d.Key.Id), d.Language))],
                    continuation, page.Complete && continuation is null, page.Cursor, page.AuthoritativeDeletes);
            }
            while (continuation is not null);
        }

        public async Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);
            var operations = batch.Operations.Select(ToDto).ToList();
            ResultList? response;
            try
            {
                response = await SendAsync<ResultList>(_http, HttpMethod.Post, $"v1/projects/{_project}/operations", new { operations }, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Timeout or SourceErrorKind.Unavailable)
            {
                // A gateway timeout or overload after the body was sent: the batch may have been applied.
                return Ambiguous(batch, ex.Error);
            }
            catch (HttpRequestException ex)
            {
                return Ambiguous(batch, new SourceError(SourceErrorKind.Network, ex.Message));
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return Ambiguous(batch, new SourceError(SourceErrorKind.Timeout, ex.Message));
            }
            var byId = (response?.Results ?? []).ToDictionary(r => r.Id);
            var results = batch.Operations.Select(op => byId.TryGetValue(op.OperationId, out var r) ? ToResult(r) : new UnitOperationResult(op.OperationId, UnitOperationOutcome.Unknown)
                { Error = new SourceError(SourceErrorKind.Unknown, "The service returned no result for this operation.") }).ToList();
            return new UnitWriteResult(results);
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            return ValueTask.CompletedTask;
        }

        private static UnitWriteResult Ambiguous(UnitWriteBatch batch, SourceError error) =>
            new([.. batch.Operations.Select(op => new UnitOperationResult(op.OperationId, UnitOperationOutcome.Unknown) { Error = error })]);

        private static UnitOperationResult ToResult(ResultDto r) => new(r.Id, r.Outcome switch
        {
            "applied" => UnitOperationOutcome.Applied,
            "alreadyApplied" => UnitOperationOutcome.AlreadyApplied,
            "conflict" => UnitOperationOutcome.Conflict,
            "notFound" => UnitOperationOutcome.NotFound,
            "refused" => UnitOperationOutcome.Refused,
            "rateLimited" => UnitOperationOutcome.RateLimited,
            "failed" => UnitOperationOutcome.Failed,
            _ => UnitOperationOutcome.Unknown,
        })
        {
            NewRevision = r.Revision,
            AssignedRemoteId = r.RemoteId,
            CurrentRemote = r.Current is null ? null : ToUnit(r.Current),
            Error = r.Error is null ? null : new SourceError(r.Outcome == "rateLimited" ? SourceErrorKind.RateLimited : SourceErrorKind.Validation, r.Error.Message ?? "The service refused it.") { RemoteCode = r.Error.Code },
        };

        private static OperationDto ToDto(UnitWriteOperation op)
        {
            static KeyDto Key(RemoteKey k) => new(k.ResourceId, k.Name, k.RemoteId);
            return op switch
            {
                PutValue v => new() { Id = v.OperationId, Type = "putValue", Key = Key(v.Key), Language = v.Language, Value = v.Value, ExpectedRevision = v.ExpectedRevision },
                PutStatus s => new() { Id = s.OperationId, Type = "putStatus", Key = Key(s.Key), Language = s.Language, Status = MeaningName(s.Meaning), ExpectedRevision = s.ExpectedRevision },
                PutComment c => new() { Id = c.OperationId, Type = "putComment", Key = Key(c.Key), Language = c.Language, Comment = c.Comment },
                CreateRemoteKey k => new() { Id = k.OperationId, Type = "createKey", Key = Key(k.Key), Values = new(k.Values) },
                RenameRemoteKey r => new() { Id = r.OperationId, Type = "renameKey", Key = Key(r.Key), NewName = r.NewName },
                DeleteRemoteKey d => new() { Id = d.OperationId, Type = "deleteKey", Key = Key(d.Key) },
                DeleteTranslation t => new() { Id = t.OperationId, Type = "deleteTranslation", Key = Key(t.Key), Language = t.Language, ExpectedRevision = t.ExpectedRevision },
                _ => throw new NotSupportedException($"{op.GetType().Name} is not part of the Toucan source protocol."),
            };
        }
    }
}
