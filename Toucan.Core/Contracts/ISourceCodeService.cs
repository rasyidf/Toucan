namespace Toucan.Core.Contracts;

/// <summary>
/// Scans source code files to find translation key references.
/// Used to identify unused keys and show where keys are used.
/// </summary>
public interface ISourceCodeService
{
    /// <summary>Scan source files under the given root for translation key usage.</summary>
    Task<SourceCodeScanResult> ScanAsync(string sourceRoot, CancellationToken cancellationToken = default);

    /// <summary>Find all usages of a specific key.</summary>
    IEnumerable<KeyUsage> FindUsages(string key);

    /// <summary>Get keys that are used in source but not in translation files.</summary>
    IEnumerable<string> GetUndefinedKeys(IEnumerable<string> translationKeys);

    /// <summary>Get translation keys that have no usage in source files.</summary>
    IEnumerable<string> GetUnusedKeys(IEnumerable<string> translationKeys);

    /// <summary>Whether a scan has been completed.</summary>
    bool HasScanData { get; }
}

/// <summary>A single usage of a translation key in source code.</summary>
public record KeyUsage(string Key, string FilePath, int Line, string LineContent);

/// <summary>Result of scanning source files.</summary>
public class SourceCodeScanResult
{
    public int FilesScanned { get; init; }
    public int KeysFound { get; init; }
    public int TotalUsages { get; init; }
    public TimeSpan Duration { get; init; }
}
