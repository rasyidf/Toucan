using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Toucan.Connector.Crowdin.Tests;

/// <summary>
/// A stateful stand-in for the parts of Crowdin's API v2 the connector uses, written from Crowdin's API documentation. It also
/// enforces what the real service enforces: the bearer token on the API host, and no Authorization header on the storage host
/// that serves exports (production-enterprise-importer.downloads.crowdin.com in Crowdin's own examples; its links are pre-signed). Check it against the real service with the opt-in live tests.
/// </summary>
internal sealed class FakeCrowdin : HttpMessageHandler
{
    public const string Token = "good-token";
    public const long ProjectId = 7;
    public const string ApiHost = "api.crowdin.com";
    public const string FileHost = "production-enterprise-importer.downloads.crowdin.com";

    internal sealed class CrowdinFile(long id, string path, byte[] source, string? exportPattern)
    {
        public long Id { get; } = id;
        public string Path { get; } = path;
        public byte[] Source { get; set; } = source;
        public string? ExportPattern { get; } = exportPattern;
        public Dictionary<string, byte[]> Translations { get; } = [];
    }

    private readonly Dictionary<string, byte[]> _downloads = [];
    private readonly Dictionary<long, byte[]> _storages = [];
    private long _nextId = 100;

    public List<CrowdinFile> Files { get; } = [];
    public List<(HttpMethod Method, Uri Url, bool HasAuthorization)> Requests { get; } = [];

    /// <summary>Answers the next request to a path containing this text with this status.</summary>
    public (string Contains, HttpStatusCode Status, int? RetryAfter)? FailNext { get; set; }

    /// <summary>The next upload is applied, then the connection drops before the answer arrives.</summary>
    public bool DropAfterNextImport { get; set; }

    /// <summary>Changes a translation just before the next export is built, as another translator would.</summary>
    public Action? BeforeNextExport { get; set; }

    public CrowdinFile AddFile(string path, string source, string? exportPattern = "/locales/%locale%/%original_file_name%", long? id = null)
    {
        var file = new CrowdinFile(id ?? ++_nextId, path, Encoding.UTF8.GetBytes(source), exportPattern);
        Files.Add(file);
        return file;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Add((request.Method, url, request.Headers.Authorization is not null));
        if (FailNext is { } fail && url.AbsolutePath.Contains(fail.Contains, StringComparison.Ordinal))
        {
            FailNext = null;
            var failed = Json(fail.Status, new { error = new { message = "Injected failure.", code = (int)fail.Status } });
            if (fail.RetryAfter is { } seconds) failed.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return failed;
        }

        if (url.Host == FileHost)
        {
            // Storage hosts reject a request that carries Crowdin's token as well as their own signature.
            if (request.Headers.Authorization is not null) return Json(HttpStatusCode.BadRequest, new { error = new { message = "Only one auth mechanism allowed." } });
            return _downloads.TryGetValue(url.AbsolutePath.TrimStart('/'), out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : Json(HttpStatusCode.NotFound, new { });
        }
        if (url.Host != ApiHost) return Json(HttpStatusCode.BadGateway, new { });
        if (request.Headers.Authorization?.ToString() != "Bearer " + Token)
            return Json(HttpStatusCode.Unauthorized, new { error = new { message = "Unauthorized", code = 401 } });

        var path = url.AbsolutePath["/api/v2/".Length..].Trim('/').Split('/');
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        return Route(request.Method, path, url, body);
    }

    private HttpResponseMessage Route(HttpMethod method, string[] p, Uri url, byte[] body)
    {
        if (method == HttpMethod.Post && p is ["storages"])
        {
            var id = ++_nextId;
            _storages[id] = body;
            return Json(HttpStatusCode.Created, new { data = new { id } });
        }
        if (method == HttpMethod.Get && p is ["projects"]) return Json(HttpStatusCode.OK, new { data = new[] { new { data = new { id = ProjectId, name = "Demo", description = "A demo project" } } } });
        if (p is not ["projects", var id2, ..] || id2 != ProjectId.ToString(CultureInfo.InvariantCulture)) return Json(HttpStatusCode.NotFound, new { error = new { message = "Project not found", code = 404 } });
        var rest = p[2..];

        if (method == HttpMethod.Get && rest.Length == 0) return Json(HttpStatusCode.OK, new { data = new
        {
            id = ProjectId, name = "Demo", sourceLanguageId = "en",
            sourceLanguage = new { id = "en", name = "English", locale = "en-US", twoLettersCode = "en" },
            targetLanguageIds = new[] { "fr", "de" },
            targetLanguages = new[]
            {
                new { id = "fr", name = "French", locale = "fr-FR", twoLettersCode = "fr" },
                new { id = "de", name = "German", locale = "de-DE", twoLettersCode = "de" },
            },
        } });

        if (method == HttpMethod.Get && rest is ["files"])
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var offset = int.Parse(query["offset"] ?? "0", CultureInfo.InvariantCulture);
            var limit = int.Parse(query["limit"] ?? "25", CultureInfo.InvariantCulture);
            return Json(HttpStatusCode.OK, new
            {
                data = Files.Skip(offset).Take(limit).Select(f => new { data = new { id = f.Id, name = System.IO.Path.GetFileName(f.Path), path = "/" + f.Path.TrimStart('/'), type = "json", revisionId = 1,
                    exportOptions = f.ExportPattern is null ? null : new { exportPattern = f.ExportPattern } } }),
                pagination = new { offset, limit },
            });
        }

        if (method == HttpMethod.Get && rest is ["files", var downloadId, "download"] && FindFile(downloadId) is { } source)
            return Json(HttpStatusCode.OK, new { data = new { url = Publish(source.Source) } });

        if (method == HttpMethod.Post && rest is ["translations", "builds", "files", var buildId] && FindFile(buildId) is { } built)
        {
            BeforeNextExport?.Invoke();
            BeforeNextExport = null;
            var language = JsonDocument.Parse(body).RootElement.GetProperty("targetLanguageId").GetString()!;
            return Json(HttpStatusCode.OK, new { data = new { url = Publish(built.Translations.GetValueOrDefault(language) ?? built.Source) } });
        }

        if (method == HttpMethod.Put && rest is ["files", var updateId] && FindFile(updateId) is { } updated)
        {
            var request = JsonDocument.Parse(body).RootElement;
            updated.Source = _storages[request.GetProperty("storageId").GetInt64()];
            if (DropAfterNextImport) { DropAfterNextImport = false; throw new HttpRequestException("The connection was reset."); }
            return Json(HttpStatusCode.OK, new { data = new { id = updated.Id } });
        }

        if (method == HttpMethod.Post && rest is ["translations", var language2] && language2 != "builds")
        {
            var request = JsonDocument.Parse(body).RootElement;
            var file = FindFile(request.GetProperty("fileId").GetInt64().ToString(CultureInfo.InvariantCulture));
            if (file is null) return Json(HttpStatusCode.NotFound, new { error = new { message = "File not found", code = 404 } });
            file.Translations[language2] = _storages[request.GetProperty("storageId").GetInt64()];
            if (DropAfterNextImport) { DropAfterNextImport = false; throw new HttpRequestException("The connection was reset."); }
            return Json(HttpStatusCode.Created, new { data = new { fileId = file.Id } });
        }

        return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found", code = 404 } });
    }

    private CrowdinFile? FindFile(string id) => Files.FirstOrDefault(f => f.Id.ToString(CultureInfo.InvariantCulture) == id);

    private string Publish(byte[] bytes)
    {
        var key = Guid.NewGuid().ToString("N");
        _downloads[key] = bytes;
        return $"https://{FileHost}/{key}";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json") };
}
