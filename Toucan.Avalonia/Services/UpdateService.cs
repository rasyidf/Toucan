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

    /// <summary>The running version with its pre-release label (1.0.0-rc.1), from the informational version.</summary>
    public static string CurrentVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public static Task<UpdateCheckResult> CheckAsync(string channel, Version current, CancellationToken ct = default) =>
        CheckAsync(channel, current.ToString(3), ct);

    public static async Task<UpdateCheckResult> CheckAsync(string channel, string current, CancellationToken ct = default)
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

    public static UpdateCheckResult Pick(JsonElement releases, bool includePreview, Version current) => Pick(releases, includePreview, current.ToString(3));

    /// <summary>
    /// Chooses the newest eligible release from the GitHub response. Pre-release labels count: 1.0.0 is newer than
    /// 1.0.0-rc.2, which is newer than 1.0.0-rc.1. Public for tests.
    /// </summary>
    public static UpdateCheckResult Pick(JsonElement releases, bool includePreview, string current)
    {
        if (!ReleaseVersion.TryParse(current, out var running)) running = new(new Version(0, 0, 0), null);
        ReleaseVersion? best = null;
        Uri? bestUrl = null;
        var bestPreview = false;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var isPre = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (isPre && !includePreview) continue;
            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!ReleaseVersion.TryParse(tag, out var version)) continue;
            if (best is { } b && version.CompareTo(b) <= 0) continue;
            best = version;
            bestPreview = isPre;
            bestUrl = release.TryGetProperty("html_url", out var u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri) ? uri : null;
        }
        return best is not { } newest
            ? new UpdateCheckResult(false, false, null, null, false)
            : new UpdateCheckResult(false, newest.CompareTo(running) > 0, newest.ToString(), bestUrl ?? new Uri(ReleasesUrl), bestPreview);
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

/// <summary>A release version with its optional pre-release label, ordered as semantic versions are.</summary>
public readonly record struct ReleaseVersion(Version Core, string? Label) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().TrimStart('v', 'V').Split('+')[0];
        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? trimmed : trimmed[..dash];
        if (!Version.TryParse(core, out var parsed)) return false;
        version = new(new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)), dash < 0 ? null : trimmed[(dash + 1)..]);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var core = Core.CompareTo(other.Core);
        if (core != 0) return core;
        if (Label == other.Label) return 0;
        if (Label is null) return 1;
        if (other.Label is null) return -1;
        var mine = Label.Split('.');
        var theirs = other.Label.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var a = int.TryParse(mine[i], out var na); var b = int.TryParse(theirs[i], out var nb);
            var order = a && b ? na.CompareTo(nb) : a ? -1 : b ? 1 : string.CompareOrdinal(mine[i], theirs[i]);
            if (order != 0) return order;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => Label is null ? Core.ToString(3) : $"{Core.ToString(3)}-{Label}";

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
}
