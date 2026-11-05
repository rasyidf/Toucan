using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Sample.SourceServer;

/// <summary>The Toucan source protocol (docs/source-protocol.md) over <see cref="SourceStore"/>.</summary>
public static class ReferenceServer
{
    public const string DefaultToken = "reference-token";

    /// <summary>Builds the app. Call <c>StartAsync</c> or <c>RunAsync</c> on the result; tests bind it to a loopback port.</summary>
    public static WebApplication Build(string[] args, SourceStore? store = null, string token = DefaultToken)
    {
        store ??= Seeded();
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();
        Map(app, store, token);
        return app;
    }

    public static SourceStore Seeded()
    {
        var store = new SourceStore();
        store.Put("app", "home.title", "en", "Welcome", "approved");
        store.Put("app", "home.title", "fr", "Bienvenue", "approved");
        store.Put("app", "home.subtitle", "en", "Translate anything");
        store.Put("app", "home.subtitle", "fr", "Traduisez tout");
        store.Put("app", "cart.empty", "en", "Your cart is empty");
        return store;
    }

    private static IResult Error(int status, string code, string message, double? retryAfter = null) =>
        Results.Json(new { error = new { code, message, retryAfterSeconds = retryAfter } }, statusCode: status);

    private static object UnitJson(StoredUnit u) => new
    {
        key = new { resource = u.Resource, name = u.Name, id = u.KeyId },
        language = u.Language,
        value = u.Value,
        revision = u.Revision,
        status = new { value = u.Status, meaning = u.Status },
        comment = u.Comment,
    };

    private static void Map(WebApplication app, SourceStore store, string token)
    {
        // Every request needs the bearer token. The error body is the protocol's.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization != $"Bearer {token}")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = new { code = "unauthenticated", message = "Sign in again." } });
                return;
            }
            await next();
        });

        var projects = app.MapGroup("/v1/projects");
        projects.MapGet("", () => Results.Json(new { items = new[] { new { id = store.ProjectId, name = store.ProjectName, description = "A sample project." } } }));

        projects.MapGet("/{project}", (string project) => project != store.ProjectId ? Error(404, "not_found", "No such project.") : Results.Json(new
        {
            id = store.ProjectId,
            name = store.ProjectName,
            sourceLanguage = store.SourceLanguage,
            languages = store.Languages.Select(l => new { code = l, name = l, isSource = l == store.SourceLanguage }),
            resources = store.Resources.Select(r => new { id = r.Id, name = r.Name }),
            permissions = new { writeValues = true, createKeys = true, renameKeys = true, deleteKeys = true, deleteTranslations = true, status = true, comments = true, conditionalWrites = true, changes = true },
            limits = new { maxBatchSize = store.MaxBatchSize, maxConcurrency = 2, requestsPerMinute = 600 },
        }));

        projects.MapGet("/{project}/units", (string project, HttpRequest request) =>
        {
            if (project != store.ProjectId) return Error(404, "not_found", "No such project.");
            var units = store.Units(request.Query["resource"].Where(v => v is not null).Select(v => v!).ToList(), request.Query["language"].Where(v => v is not null).Select(v => v!).ToList());
            var limit = int.TryParse(request.Query["limit"], CultureInfo.InvariantCulture, out var l) ? Math.Clamp(l, 1, 1000) : store.PageSize;
            var start = int.TryParse(request.Query["cursor"].ToString().TrimStart('c'), CultureInfo.InvariantCulture, out var s) ? s : 0;
            var page = units.Skip(start).Take(limit).ToList();
            var next = start + page.Count < units.Count ? "c" + (start + page.Count).ToString(CultureInfo.InvariantCulture) : null;
            return Results.Json(new { items = page.Select(UnitJson), nextCursor = next, complete = next is null, deltaCursor = next is null ? "s" + store.Cursor.ToString(CultureInfo.InvariantCulture) : null });
        });

        projects.MapGet("/{project}/changes", (string project, HttpRequest request) =>
        {
            if (project != store.ProjectId) return Error(404, "not_found", "No such project.");
            if (!long.TryParse(request.Query["cursor"].ToString().TrimStart('s'), CultureInfo.InvariantCulture, out var since)) return Error(422, "invalid", "The cursor is not valid.");
            var (changes, cursor) = store.ChangesSince(since);
            return Results.Json(new
            {
                changed = changes.Where(c => c.Unit is not null).Select(c => UnitJson(c.Unit!)),
                deleted = changes.Where(c => c.Unit is null).Select(c => new { key = new { resource = c.DeletedResource, name = c.DeletedName, id = c.DeletedKeyId }, language = c.DeletedLanguage }),
                nextCursor = (string?)null,
                complete = true,
                cursor = "s" + cursor.ToString(CultureInfo.InvariantCulture),
                authoritativeDeletes = true,
            });
        });

        projects.MapPost("/{project}/operations", (string project, OperationsRequest body) =>
        {
            if (project != store.ProjectId) return Error(404, "not_found", "No such project.");
            if (body.Operations is not { Count: > 0 }) return Error(422, "invalid", "Send at least one operation.");
            if (body.Operations.Count > store.MaxBatchSize) return Error(422, "invalid", $"At most {store.MaxBatchSize} operations per request.");
            var results = store.Apply(body.Operations).Select(r => new
            {
                id = r.Id,
                outcome = r.Outcome,
                revision = r.Revision,
                remoteId = r.RemoteId,
                current = r.Current is null ? null : UnitJson(r.Current),
                error = r.ErrorCode is null ? null : new { code = r.ErrorCode, message = r.ErrorMessage },
            });
            return Results.Json(new { results });
        });
    }

    public sealed record OperationsRequest(List<WriteOperation>? Operations);
}
