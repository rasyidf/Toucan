using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Toucan.Avalonia.Services;

/// <summary>Result of an update check. <see cref="Failed"/> means the check itself did not work (offline, rate limited).</summary>
public sealed record UpdateCheckResult(bool Failed, bool IsNewer, string? Version, Uri? Url, bool IsPreview);

/// <summary>Looks at the GitHub releases of Toucan and says whether something newer than the running version exists. It never downloads or installs anything.</summary>
public static class UpdateService
{
    public const string ReleasesUrl = "https://github.com/rasyidf/Toucan/releases";
    private const string Api = "https://api.github.com/repos/rasyidf/Toucan/releases?per_page=20";

    private static readonly HttpClient s_http = Create();

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Toucan", "update-check"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static async Task<UpdateCheckResult> CheckAsync(string channel, Version current, CancellationToken ct = default)
    {
        try
        {
            using var stream = await s_http.GetStreamAsync(Api, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var preview = string.Equals(channel, "Preview", StringComparison.OrdinalIgnoreCase);
            return Pick(doc.RootElement, preview, current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new UpdateCheckResult(true, false, null, null, false);
        }
    }

    /// <summary>Chooses the newest eligible release from the GitHub response. Public for tests.</summary>
    public static UpdateCheckResult Pick(JsonElement releases, bool includePreview, Version current)
    {
        Version? best = null;
        Uri? bestUrl = null;
        var bestPreview = false;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var isPre = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (isPre && !includePreview) continue;
            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!TryParseTag(tag, out var version)) continue;
            if (best is not null && version <= best) continue;
            best = version;
            bestPreview = isPre;
            bestUrl = release.TryGetProperty("html_url", out var u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri) ? uri : null;
        }
        return best is null
            ? new UpdateCheckResult(false, false, null, null, false)
            : new UpdateCheckResult(false, best > current, best.ToString(3), bestUrl ?? new Uri(ReleasesUrl), bestPreview);
    }

    /// <summary>"v0.24.0", "0.24" or "v0.24.0-preview.1" → 0.24.0 (a suffix after '-' or '+' is ignored).</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var core = tag.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var parsed) && (version = parsed) is not null;
    }
}
