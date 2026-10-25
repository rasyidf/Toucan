using System.Diagnostics;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Bounded project orchestration. Scanners only parse file text; cache replacement is atomic.</summary>
public sealed class SourceCodeService(IEnumerable<ISourceScanner>? scanners = null, IEnumerable<IFrameworkProfile>? profiles = null) : ISourceCodeService, IProjectSourceCodeService
{
    public const int MaxFiles = 20_000, MaxFileBytes = 2 * 1024 * 1024;
    public const long MaxTotalBytes = 100 * 1024 * 1024;
    private static readonly string[] Defaults = [".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte", ".go", ".html", ".erb", ".rb", ".cs", ".py", ".php", ".swift", ".dart", ".java", ".kt"];
    private static readonly string[] Excluded = ["node_modules", ".git", "dist", "build", "out", ".next", "__pycache__", "bin", "obj", "Pods", ".dart_tool"];
    private IReadOnlyDictionary<string, IReadOnlyList<KeyUsage>> _usages = new Dictionary<string, IReadOnlyList<KeyUsage>>();
    private readonly object _gate = new();
    private long _generation;
    private ProjectSourceScanResult? _lastScan;
    public ProjectSourceScanResult? LastScan { get { lock (_gate) return _lastScan; } }
    public bool HasScanData => LastScan != null;
    public void Reset() { lock (_gate) { _generation++; _lastScan = null; _usages = new Dictionary<string, IReadOnlyList<KeyUsage>>(); } }
    public async Task<SourceCodeScanResult> ScanAsync(string sourceRoot, CancellationToken cancellationToken = default)
    {
        var result = await ScanAsync(new SourceScanRequest(sourceRoot), cancellationToken).ConfigureAwait(false);
        return new() { FilesScanned = result.FilesScanned, KeysFound = result.References.Select(r => (r.Package, r.Key)).Distinct().Count(), TotalUsages = result.References.Count, Duration = result.Duration };
    }
    public async Task<ProjectSourceScanResult> ScanAsync(SourceScanRequest request, CancellationToken cancellationToken = default)
    {
        long generation;
        lock (_gate) generation = ++_generation;
        var result = await Task.Run(() => Scan(request, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        var mapped = new Dictionary<string, List<KeyUsage>>(StringComparer.Ordinal);
        var diagnostics = result.Diagnostics.ToList();
        var keyIndex = (request.ResourceKeys ?? []).GroupBy(k => (k.Package, k.Key)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var reference in result.References)
        {
            var matches = keyIndex.GetValueOrDefault((reference.Package ?? "main", reference.Key), []);
            if (matches.Count > 1) { diagnostics.Add(new("source.ambiguous-key", "Source syntax cannot distinguish these typed resource keys.", reference.File, reference.Line, reference.Column)); continue; }
            var display = matches.Count == 1 ? matches[0].DisplayKey : reference.Package is null or "main" ? reference.Key : reference.Package + ":" + reference.Key;
            if (!mapped.TryGetValue(display, out var usages)) mapped[display] = usages = [];
            usages.Add(new(display, reference.File, reference.Line, reference.LineContent) { Package = reference.Package, Column = reference.Column });
        }
        result = result with { Diagnostics = diagnostics };
        lock (_gate)
            if (generation == _generation)
            {
                _usages = mapped.ToDictionary(p => p.Key, p => (IReadOnlyList<KeyUsage>)p.Value);
                _lastScan = result;
            }
        return result;
    }
    private ProjectSourceScanResult Scan(SourceScanRequest request, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew(); var references = new List<SourceReference>(); var diagnostics = new List<SourceScanDiagnostic>();
        var count = 0; long bytes = 0; var complete = true; var cancelled = false;
        try
        {
            var features = (profiles ?? []).FirstOrDefault(p => p.Id == request.Framework) as IFrameworkFeatures;
            var id = features?.ScannerId ?? "generic";
            var scanner = (scanners ?? []).FirstOrDefault(s => s.Id == id);
            scanner ??= id == "generic" ? new GenericSourceScanner() : null;
            if (scanner == null) return new([], [new("source.scanner-unavailable", $"Scanner '{id}' is not registered.")], false, false, 0, timer.Elapsed);
            if (features == null) diagnostics.Add(new("source.generic-fallback", "Generic scanner has no framework binding analysis; unused-key classification is disabled."));
            var options = request.Options ?? new FrameworkOptions();
            if (string.IsNullOrWhiteSpace(options.DefaultPackage) || options.KeyPrefix == null)
                return new([], [new("source.invalid-options", "Default source package must be nonempty and key prefix must be text.")], false, false, 0, timer.Elapsed);
            if (options.ExtensionData is { Count: > 0 }) diagnostics.Add(new("source.unsupported-options", "Unknown framework options were preserved but not applied."));
            var extensions = (request.Extensions is { Count: > 0 } ? request.Extensions : Defaults).Select(e => e.StartsWith('.') ? e : "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excludes = Excluded.Concat(request.Excludes ?? []).Select(e => e.Replace('\\', '/')).ToList();
            if ((request.Includes?.Count ?? 0) > 128 || excludes.Count > 128 || (request.SourceRoots?.Count ?? 0) > 128)
                throw new ArgumentException("At most 128 source roots/includes/excludes are supported.");
            var include = (request.Includes is { Count: > 0 } ? request.Includes : ["**"]).Select(Glob).ToList();
            var excludedGlobs = excludes.Where(e => e.Contains('/') || e.Contains('*') || e.Contains('?')).Select(Glob).ToList();
            var excludedNames = excludes.Where(e => !e.Contains('/') && !e.Contains('*') && !e.Contains('?')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var stack = new Stack<string>((request.SourceRoots is { Count: > 0 } ? request.SourceRoots : ["."]).Select(r => Path.GetFullPath(Path.Combine(request.Root, r))));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested(); var dir = stack.Pop();
                if (!seen.Add(dir)) continue;
                if (seen.Count > 50_000) { diagnostics.Add(new("source.directory-limit", "Source directory limit exceeded.")); complete = false; break; }
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { diagnostics.Add(new("source.link-skipped", "Source directory link was not followed.", dir)); complete = false; continue; }
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (references.Count >= 200_000 || diagnostics.Count >= 10_000) { diagnostics.Add(new("source.result-limit", "Source result limit exceeded.")); complete = false; stack.Clear(); break; }
                        var relative = Path.GetRelativePath(request.Root, entry).Replace('\\', '/');
                        if (excludedGlobs.Any(g => g.IsMatch(relative)) || excludedNames.Contains(Path.GetFileName(entry))) continue;
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { diagnostics.Add(new("source.link-skipped", "Source link was not followed.", relative)); complete = false; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { stack.Push(entry); continue; }
                        if (!extensions.Contains(Path.GetExtension(entry)) || !include.Any(g => g.IsMatch(relative))) continue;
                        if (count >= MaxFiles) { diagnostics.Add(new("source.file-limit", "Source file limit exceeded.", relative)); complete = false; stack.Clear(); break; }
                        var length = new FileInfo(entry).Length;
                        if (length > MaxFileBytes || bytes + length > MaxTotalBytes) { diagnostics.Add(new("source.size-limit", "Source size limit exceeded.", relative)); complete = false; continue; }
                        try
                        {
                            var text = ReadBounded(entry);
                            if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxFileBytes) { diagnostics.Add(new("source.size-limit", "Decoded source size limit exceeded.", relative)); complete = false; continue; }
                            bytes += length; count++;
                            var parsed = scanner.Extract(new(relative, text, options, ct));
                            references.AddRange(parsed.References); diagnostics.AddRange(parsed.Diagnostics);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                        { diagnostics.Add(new("source.read-failed", ex.Message, relative)); complete = false; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { diagnostics.Add(new("source.directory-failed", ex.Message, dir)); complete = false; }
            }
            if (request.Packages is { Count: > 0 })
                foreach (var reference in references.Where(r => !request.Packages.Contains(r.Package)))
                    diagnostics.Add(new("source.unknown-package", $"Unknown source package '{reference.Package}'. Configure the default package or namespace binding.", reference.File, reference.Line, reference.Column));
        }
        catch (OperationCanceledException) { complete = false; cancelled = true; diagnostics.Add(new("source.cancelled", "Source scan was cancelled.")); }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        { complete = false; diagnostics.Add(new("source.invalid-scope", ex.Message)); }
        return new(references, diagnostics, complete, cancelled, count, timer.Elapsed);
    }
    private static Regex Glob(string pattern)
    {
        if (pattern.Length > 512) throw new ArgumentException("Source glob is too long.");
        var escaped = Regex.Escape(pattern.Replace('\\', '/')).Replace("\\*\\*/", "<DIR>", StringComparison.Ordinal).Replace("\\*\\*", "<ALL>", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal).Replace("\\?", "[^/]", StringComparison.Ordinal).Replace("<DIR>", "(?:.*/)?", StringComparison.Ordinal).Replace("<ALL>", ".*", StringComparison.Ordinal);
        return new("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }
    private static string ReadBounded(string file)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length > MaxFileBytes) throw new FormatException("Source file grew beyond its size limit.");
        var data = new byte[(int)stream.Length + 1]; var read = 0;
        while (read < data.Length) { var count = stream.Read(data, read, data.Length - read); if (count == 0) break; read += count; }
        if (read == data.Length) throw new FormatException("Source file changed during scanning; scan again.");
        using var reader = new StreamReader(new MemoryStream(data, 0, read), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    public IEnumerable<KeyUsage> FindUsages(string key) { lock (_gate) return _usages.GetValueOrDefault(key, []); }
    public IEnumerable<string> GetAllKeys() { lock (_gate) return _usages.Keys.ToList(); }
    public IEnumerable<string> GetUndefinedKeys(IEnumerable<string> translationKeys) { lock (_gate) return _usages.Keys.Except(translationKeys, StringComparer.Ordinal).ToList(); }
    public IEnumerable<string> GetUnusedKeys(IEnumerable<string> translationKeys) { lock (_gate) return _lastScan?.CanClassifyUnused == true ? translationKeys.Except(_usages.Keys, StringComparer.Ordinal).ToList() : []; }
}
