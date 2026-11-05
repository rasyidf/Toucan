using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Plugins.Sources;

namespace Toucan.Connector.Crowdin;

/// <summary>
/// Crowdin (API v2) as a document source. A Crowdin file is a resource; the project's source language and each mapped target
/// language give one document per file, so Toucan's own format strategies parse and write them. Reads export the file in the
/// language (source files are downloaded as they are); writes upload a storage and import it as the language's translation, or
/// update the source file. Creating and deleting files stays in Crowdin.
/// </summary>
/// <remarks>
/// A document's version is the SHA-256 of its exported bytes: Crowdin has no per-translation revision. Writes are therefore
/// conditional by re-exporting first, which narrows a race but cannot close it; the conformance suite's strict conflict check
/// is skipped for that reason in the connector's tests. Crowdin offers no idempotency keys, so a write whose answer is lost
/// is reported as <see cref="UnitOperationOutcome.Unknown"/> and read back before it is repeated.
///
/// Checked against Crowdin's API reference: bearer token, <c>GET /files/{id}/download</c> for source files, <c>POST
/// /translations/builds/files/{id}</c> for a translation, <c>POST /storages</c> with <c>Crowdin-API-FileName</c>, import with
/// <c>POST /translations/{languageId}</c>, <c>PUT /files/{id}</c> with <c>updateOption</c>, the <c>{"error":{"message","code"}}</c>
/// body, and 429 above 20 simultaneous requests. A file's <c>exportOptions.exportPattern</c> is real (confirmed against a live
/// project); without one, paths fall back to <c>{language}/{source path}</c>. Directory and branch patterns are not read.
/// </remarks>
public sealed class CrowdinAdapter : IContentSourceAdapter, ISupportsProjectDiscovery
{
    public const string AdapterId = "crowdin.connector.api";

    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = AdapterId,
        DisplayName = "Crowdin",
        Icon = "Cloud",
        DefaultBaseUrl = new Uri("https://api.crowdin.com/api/v2/"),
        // Crowdin Enterprise lives at https://{organization}.api.crowdin.com/api/v2/.
        AllowsCustomBaseUrl = true,
        AuthMethods = [new ApiTokenAuth("token", "Personal access token", TokenHelpUrl: new Uri("https://crowdin.com/settings#api-key"))],
        Capabilities = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.ProjectDiscovery,
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
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await GetAsync<Page<ProjectDto>>(http, $"projects?limit={PageSize}&offset={offset}", cancellationToken).ConfigureAwait(false);
            foreach (var item in page?.Data ?? []) yield return new(item.Data.Id.ToString(CultureInfo.InvariantCulture), item.Data.Name, item.Data.Description);
            if ((page?.Data.Count ?? 0) < PageSize) yield break;
        }
    }

    // ───────────── wire format (the parts used) ─────────────

    internal const int PageSize = 100;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed record Wrapper<T>(T Data);
    private sealed record Page<T>(List<Wrapper<T>> Data);
    private sealed record ProjectDto(long Id, string Name, string? Description, string? SourceLanguageId, List<string>? TargetLanguageIds, List<LanguageDto>? TargetLanguages, LanguageDto? SourceLanguage);
    private sealed record LanguageDto(string Id, string? Name, string? Locale, string? TwoLettersCode, string? ThreeLettersCode, string? AndroidCode, string? OsxCode, string? OsxLocale);
    private sealed record ExportOptions(string? ExportPattern);
    private sealed record FileDto(long Id, string Name, string Path, string? Type, ExportOptions? ExportOptions);
    private sealed record UrlDto(string Url);
    private sealed record StorageDto(long Id);
    private sealed record ErrorBody(ErrorItem? Error);
    private sealed record ErrorItem(string? Message, int? Code);

    private static Uri Normalize(Uri baseUrl) => baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");

    private static async Task<T?> GetAsync<T>(HttpClient http, string path, CancellationToken ct) => await SendAsync<T>(http, HttpMethod.Get, path, null, ct).ConfigureAwait(false);

    private static async Task<T?> SendAsync<T>(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }

    private static async Task<SourceException> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct).ConfigureAwait(false))?.Error?.Message; }
        catch (JsonException) { }
        var kind = (int)response.StatusCode switch
        {
            401 => SourceErrorKind.Authentication,
            403 => SourceErrorKind.Authorization,
            404 => SourceErrorKind.NotFound,
            409 => SourceErrorKind.Conflict,
            400 or 422 => SourceErrorKind.Validation,
            408 or 504 => SourceErrorKind.Timeout,
            429 => SourceErrorKind.RateLimited,
            >= 500 => SourceErrorKind.Unavailable,
            _ => SourceErrorKind.Unknown,
        };
        return new SourceException(new SourceError(kind, message ?? $"Crowdin answered {(int)response.StatusCode} {response.ReasonPhrase}.") { RetryAfter = response.Headers.RetryAfter?.Delta });
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// The path of a file in a language, from the file's export pattern (<c>/locales/%locale%/%original_file_name%</c>). Without a
    /// pattern Crowdin writes every language to the source path; Toucan then puts them under a folder named for the language.
    /// </summary>
    internal static string DocumentPath(FileDtoView file, LanguageView language, string sourceLanguage)
    {
        var source = file.Path.TrimStart('/');
        if (string.Equals(language.Id, sourceLanguage, StringComparison.Ordinal)) return source;
        var pattern = file.ExportPattern;
        if (string.IsNullOrWhiteSpace(pattern)) return $"{language.Id}/{source}";
        var slash = source.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : source[..slash];
        var name = slash < 0 ? source : source[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        var extension = dot < 0 ? string.Empty : name[(dot + 1)..];
        var locale = language.Locale ?? language.Id;
        return pattern.TrimStart('/')
            .Replace("%original_path%", directory, StringComparison.Ordinal)
            .Replace("%original_file_name%", name, StringComparison.Ordinal)
            .Replace("%file_name%", stem, StringComparison.Ordinal)
            .Replace("%file_extension%", extension, StringComparison.Ordinal)
            .Replace("%locale_with_underscore%", locale.Replace('-', '_'), StringComparison.Ordinal)
            .Replace("%two_letters_code%", language.TwoLetters ?? language.Id, StringComparison.Ordinal)
            .Replace("%three_letters_code%", language.ThreeLetters ?? language.Id, StringComparison.Ordinal)
            .Replace("%android_code%", language.Android ?? language.Id, StringComparison.Ordinal)
            .Replace("%osx_code%", language.Osx ?? language.Id, StringComparison.Ordinal)
            .Replace("%osx_locale%", language.OsxLocale ?? language.Id, StringComparison.Ordinal)
            .Replace("%locale%", locale, StringComparison.Ordinal)
            .Replace("//", "/", StringComparison.Ordinal)
            .TrimStart('/');
    }

    internal sealed record FileDtoView(long Id, string Path, string? ExportPattern);
    internal sealed record LanguageView(string Id, string? Locale, string? TwoLetters, string? ThreeLetters, string? Android, string? Osx, string? OsxLocale);

    // ───────────── session ─────────────

    private sealed class Session : IDocumentSourceSession
    {
        private sealed record Entry(RemoteDocumentRef Ref, long FileId, string Language, bool IsSource);

        private readonly ISourceSessionContext _context;
        private readonly HttpClient _http;
        private readonly string _project;
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (byte[] Bytes, string Version)> _content = new(StringComparer.Ordinal);
        private ProjectDto? _info;
        private bool _listed;

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
                var project = await ProjectAsync(cancellationToken).ConfigureAwait(false);
                return SourceTestResult.Ok($"Connected to {project.Name}.");
            }
            catch (SourceException ex)
            {
                return SourceTestResult.Failed(ex.Error);
            }
        }

        public async Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken)
        {
            var project = await ProjectAsync(cancellationToken).ConfigureAwait(false);
            var languages = new List<RemoteLanguage> { new(project.SourceLanguageId ?? "en", project.SourceLanguage?.Name, IsSource: true) };
            languages.AddRange((project.TargetLanguages ?? []).Select(l => new RemoteLanguage(l.Id, l.Name)));
            if (project.TargetLanguages is not { Count: > 0 })
                languages.AddRange((project.TargetLanguageIds ?? []).Select(id => new RemoteLanguage(id)));
            var files = await FilesAsync(cancellationToken).ConfigureAwait(false);
            return new SourceProjectInfo(project.Id.ToString(CultureInfo.InvariantCulture), project.Name, project.SourceLanguageId, languages,
                [.. files.Select(f => new RemoteResource(f.Id.ToString(CultureInfo.InvariantCulture), f.Name, f.Path))],
                SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments, new SourceLimits(MaxBatchSize: 1, MaxConcurrency: 2));
        }

        public async IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            // A listing always reads the project again: files are added and removed in Crowdin while Toucan is open.
            await EnsureListedAsync(cancellationToken, force: true).ConfigureAwait(false);
            var wanted = _entries.Values.Where(e => request.Prefix is null || e.Ref.Path.StartsWith(request.Prefix, StringComparison.Ordinal))
                .OrderBy(e => e.Ref.Path, StringComparer.Ordinal).ToList();
            var start = request.ContinuationToken is { } token ? wanted.FindIndex(e => string.CompareOrdinal(e.Ref.Path, token) > 0) : 0;
            if (start < 0) start = wanted.Count;
            while (true)
            {
                var page = wanted.Skip(start).Take(PageSize).ToList();
                start += page.Count;
                var infos = new List<RemoteDocumentInfo>();
                foreach (var entry in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var content = await ContentAsync(entry, cancellationToken).ConfigureAwait(false);
                    infos.Add(new(entry.Ref, content.Version, content.Bytes.Length));
                }
                var done = start >= wanted.Count;
                yield return new(infos, done ? null : page[^1].Ref.Path, done);
                if (done) yield break;
            }
        }

        public async Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            var entry = await FindAsync(document.Path, cancellationToken).ConfigureAwait(false);
            if (entry is null)
                throw new SourceException(new SourceError(SourceErrorKind.NotFound, $"{document.Path} is not in the Crowdin project."));
            var content = await ContentAsync(entry, cancellationToken, fresh: true).ConfigureAwait(false);
            return new(entry.Ref, content.Bytes, content.Version);
        }

        public async Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(write);
            var entry = await FindAsync(write.Ref.Path, cancellationToken).ConfigureAwait(false);
            if (entry is null)
                return Refused($"{write.Ref.Path} is not a file or language of this Crowdin project. Create the file in Crowdin first; Toucan only updates existing ones.");
            if (write.ExpectedVersion is null)
                return new(UnitOperationOutcome.Conflict, Error: new SourceError(SourceErrorKind.Conflict, $"{write.Ref.Path} already exists in Crowdin."));

            try
            {
                var current = await ContentAsync(entry, cancellationToken, fresh: true).ConfigureAwait(false);
                if (current.Version != write.ExpectedVersion)
                    return new(UnitOperationOutcome.Conflict, current.Version, new SourceError(SourceErrorKind.Conflict, $"{write.Ref.Path} changed in Crowdin."));

                var storage = await UploadStorageAsync(entry, write.Bytes, cancellationToken).ConfigureAwait(false);
                if (entry.IsSource)
                    await SendAsync<object>(_http, HttpMethod.Put, $"projects/{_project}/files/{entry.FileId}", new { storageId = storage, updateOption = "keep_translations_and_approvals" }, cancellationToken).ConfigureAwait(false);
                else
                    await SendAsync<object>(_http, HttpMethod.Post, $"projects/{_project}/translations/{Uri.EscapeDataString(entry.Language)}",
                        new { storageId = storage, fileId = entry.FileId, importEqSuggestions = false, autoApproveImported = false }, cancellationToken).ConfigureAwait(false);

                // Crowdin may normalise what it imported, so the version comes from what it now exports.
                var after = await ContentAsync(entry, cancellationToken, fresh: true).ConfigureAwait(false);
                return new(UnitOperationOutcome.Applied, after.Version);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Validation or SourceErrorKind.Authorization or SourceErrorKind.NotFound)
            {
                return new(UnitOperationOutcome.Refused, Error: ex.Error);
            }
            catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Timeout or SourceErrorKind.Unavailable)
            {
                return new(UnitOperationOutcome.Unknown, Error: ex.Error);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return new(UnitOperationOutcome.Unknown, Error: new SourceError(SourceErrorKind.Network, ex.Message));
            }
        }

        public Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Refused("Delete files in Crowdin itself; Toucan does not remove files or translations there."));

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            return ValueTask.CompletedTask;
        }

        // ───────────── helpers ─────────────

        private static DocumentWriteResult Refused(string message) =>
            new(UnitOperationOutcome.Refused, Error: new SourceError(SourceErrorKind.Unsupported, message));

        private static string Key(string path) => path.Replace('\\', '/').TrimStart('/');

        private async Task<ProjectDto> ProjectAsync(CancellationToken ct)
        {
            if (_info is not null) return _info;
            var wrapper = await GetAsync<Wrapper<ProjectDto>>(_http, $"projects/{_project}", ct).ConfigureAwait(false)
                ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "Crowdin returned no project."));
            return _info = wrapper.Data;
        }

        private async Task<List<FileDto>> FilesAsync(CancellationToken ct)
        {
            var files = new List<FileDto>();
            for (var offset = 0; ; offset += PageSize)
            {
                var page = await GetAsync<Page<FileDto>>(_http, $"projects/{_project}/files?limit={PageSize}&offset={offset}", ct).ConfigureAwait(false);
                files.AddRange((page?.Data ?? []).Select(w => w.Data));
                if ((page?.Data.Count ?? 0) < PageSize) return files;
            }
        }

        private async Task<Entry?> FindAsync(string path, CancellationToken ct)
        {
            await EnsureListedAsync(ct).ConfigureAwait(false);
            if (_entries.TryGetValue(Key(path), out var found)) return found;
            // Added in Crowdin since the last listing?
            await EnsureListedAsync(ct, force: true).ConfigureAwait(false);
            return _entries.GetValueOrDefault(Key(path));
        }

        private async Task EnsureListedAsync(CancellationToken ct, bool force = false)
        {
            if (_listed && !force) return;
            _entries.Clear();
            _content.Clear();
            var project = await ProjectAsync(ct).ConfigureAwait(false);
            var sourceLanguage = project.SourceLanguageId ?? "en";
            var mapped = _context.Connection.Languages.Values.ToHashSet(StringComparer.Ordinal);
            var languages = new List<LanguageView> { View(project.SourceLanguage ?? new LanguageDto(sourceLanguage, null, null, null, null, null, null, null)) };
            languages.AddRange((project.TargetLanguages ?? []).Select(View));
            if (project.TargetLanguages is not { Count: > 0 }) languages.AddRange((project.TargetLanguageIds ?? []).Select(id => new LanguageView(id, null, null, null, null, null, null)));
            foreach (var file in await FilesAsync(ct).ConfigureAwait(false))
            {
                var view = new FileDtoView(file.Id, file.Path, file.ExportOptions?.ExportPattern);
                foreach (var language in languages.Where(l => l.Id == sourceLanguage || mapped.Count == 0 || mapped.Contains(l.Id)))
                {
                    var path = DocumentPath(view, language, sourceLanguage);
                    var entry = new Entry(new RemoteDocumentRef(path, language.Id, file.Id.ToString(CultureInfo.InvariantCulture)), file.Id, language.Id, language.Id == sourceLanguage);
                    if (!_entries.TryAdd(Key(path), entry))
                        throw new SourceException(new SourceError(SourceErrorKind.Validation,
                            $"{file.Path} would write two languages to {path}. Set a language placeholder such as %locale% in the file's export pattern in Crowdin."));
                }
            }
            _listed = true;
        }

        private static LanguageView View(LanguageDto l) => new(l.Id, l.Locale, l.TwoLettersCode, l.ThreeLettersCode, l.AndroidCode, l.OsxCode, l.OsxLocale);

        private async Task<(byte[] Bytes, string Version)> ContentAsync(Entry entry, CancellationToken ct, bool fresh = false)
        {
            var key = Key(entry.Ref.Path);
            if (!fresh && _content.TryGetValue(key, out var cached)) return cached;
            var export = entry.IsSource
                ? await GetAsync<Wrapper<UrlDto>>(_http, $"projects/{_project}/files/{entry.FileId}/download", ct).ConfigureAwait(false)
                : await SendAsync<Wrapper<UrlDto>>(_http, HttpMethod.Post, $"projects/{_project}/translations/builds/files/{entry.FileId}", new { targetLanguageId = entry.Language }, ct).ConfigureAwait(false);
            var url = export?.Data.Url ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "Crowdin returned no download address."));
            using var response = await _http.GetAsync(new Uri(url), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return _content[key] = (bytes, Hash(bytes));
        }

        private async Task<long> UploadStorageAsync(Entry entry, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "storages") { Content = new ByteArrayContent(bytes.ToArray()) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Headers.TryAddWithoutValidation("Crowdin-API-FileName", Uri.EscapeDataString(Path.GetFileName(entry.Ref.Path)));
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
            var stored = await response.Content.ReadFromJsonAsync<Wrapper<StorageDto>>(Json, ct).ConfigureAwait(false);
            return stored?.Data.Id ?? throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "Crowdin did not return the stored file."));
        }
    }
}
