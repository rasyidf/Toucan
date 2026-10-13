using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>One line of a plugin's diagnostic log.</summary>
public sealed record DiagnosticLine(DateTimeOffset Time, string Source, DiagnosticLevel Level, string Message);

/// <summary>
/// Collects what plugins log and builds the report users can copy into a bug report. Everything is masked first: the values
/// of secrets that passed through the host, bearer tokens, <c>key=value</c> credentials, URL passwords, long token-like
/// strings and the user's home folder.
/// </summary>
public interface IDiagnosticsService
{
    string Redact(string text);

    /// <summary>Masks <paramref name="value"/> in everything redacted from now on. Very short values are ignored.</summary>
    void RegisterSecret(string value);

    void Write(string source, DiagnosticLevel level, string message);

    IReadOnlyList<DiagnosticLine> Recent(string? source = null);

    /// <summary>A redacted text report: the application, the platform, every plugin with its state, and each plugin's recent log.</summary>
    string BuildReport(IPluginCatalog? catalog = null);
}

public sealed partial class DiagnosticsService : IDiagnosticsService
{
    private const int MaxLines = 500;
    private const int MinSecretLength = 4;

    private readonly ConcurrentQueue<DiagnosticLine> _lines = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);

    public void RegisterSecret(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < MinSecretLength) return;
        lock (_gate) _secrets.Add(value);
    }

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string[] secrets;
        lock (_gate) secrets = [.. _secrets.OrderByDescending(s => s.Length)];
        // Longest first, so a secret that contains another is masked whole.
        foreach (var secret in secrets) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);

        text = UrlPassword().Replace(text, "$1[redacted]@");
        text = BearerToken().Replace(text, "$1 [redacted]");
        text = KeyValueCredential().Replace(text, "$1$2[redacted]");
        text = LongToken().Replace(text, "[redacted]");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 3) text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public void Write(string source, DiagnosticLevel level, string message)
    {
        _lines.Enqueue(new DiagnosticLine(DateTimeOffset.Now, source, level, Redact(message)));
        while (_lines.Count > MaxLines && _lines.TryDequeue(out _)) { }
    }

    public IReadOnlyList<DiagnosticLine> Recent(string? source = null) =>
        [.. _lines.Where(l => source is null || string.Equals(l.Source, source, StringComparison.OrdinalIgnoreCase))];

    public string BuildReport(IPluginCatalog? catalog = null)
    {
        var report = new StringBuilder();
        var assembly = typeof(DiagnosticsService).Assembly;
        report.AppendLine("Toucan diagnostics");
        report.AppendLine(CultureInfo.InvariantCulture, $"Generated: {DateTimeOffset.Now:O}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Core: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString()}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Plugin API: {PluginApi.Current}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}");

        report.AppendLine().AppendLine("Plugins");
        foreach (var plugin in catalog?.Plugins ?? [])
        {
            var version = plugin.Manifest?.Version is { Length: > 0 } v ? $" {v}" : string.Empty;
            report.AppendLine(CultureInfo.InvariantCulture, $"- {plugin.DisplayId}{version}: {plugin.Status}{(plugin.Error is { Length: > 0 } e ? $" ({e})" : string.Empty)}");
            foreach (var line in Recent(plugin.DisplayId).TakeLast(40))
                report.AppendLine(CultureInfo.InvariantCulture, $"    {line.Time:HH:mm:ss} {line.Level,-7} {line.Message}");
        }
        if (catalog is null || catalog.Plugins.Count == 0) report.AppendLine("(none)");

        return Redact(report.ToString());
    }

    [GeneratedRegex(@"(://[^/\s:@]+:)[^@\s/]+@", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlPassword();

    [GeneratedRegex(@"\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(\b(?:api[_-]?key|access[_-]?token|auth[_-]?token|token|secret|password|passwd|pwd|authorization)\b""?\s*[=:]\s*""?)[^\s"",;&]+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyValueCredential();

    [GeneratedRegex(@"(?<![A-Za-z0-9_\-/\\.])[A-Za-z0-9_\-]{32,}(?![A-Za-z0-9_\-/\\.])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LongToken();
}

/// <summary>Wraps a plugin's logger so what it writes is masked before it reaches any log, and is kept for the diagnostics report.</summary>
internal sealed class RedactingLogger(ILogger inner, IDiagnosticsService diagnostics, string source) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel) || logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null) message += Environment.NewLine + exception;
        var redacted = diagnostics.Redact(message);

        if (logLevel >= LogLevel.Information)
            diagnostics.Write(source, logLevel >= LogLevel.Error ? DiagnosticLevel.Error : logLevel == LogLevel.Warning ? DiagnosticLevel.Warning : DiagnosticLevel.Info, redacted);
        // The inner logger gets the masked text only, without the exception object (whose own ToString could carry a secret).
        if (inner.IsEnabled(logLevel)) inner.Log(logLevel, eventId, redacted, null, static (s, _) => s);
    }
}
