using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public sealed record SourceScanRequest(string Root, string? Framework = null, FrameworkOptions? Options = null,
    IReadOnlyList<string>? Packages = null, IReadOnlyList<string>? Includes = null, IReadOnlyList<string>? Excludes = null,
    IReadOnlyList<string>? Extensions = null, IReadOnlyList<string>? SourceRoots = null, IReadOnlyList<SourceScanKey>? ResourceKeys = null);
public sealed record SourceScanKey(string Package, string Key, string DisplayKey, string PathToken);
public sealed record SourceReference(string Key, string? Package, string File, int Offset, int Line, int Column,
    IReadOnlyList<ResourcePathSegment> Path, string LineContent = "");
public sealed record SourceScanDiagnostic(string Code, string Message, string? File = null, int? Line = null, int? Column = null);
public sealed record SourceScanFile(string File, string Text, FrameworkOptions Options, CancellationToken CancellationToken)
{
    public IReadOnlyList<int> LineStarts { get; } = [0, .. Text.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i + 1)];
}
public sealed record SourceFileResult(IReadOnlyList<SourceReference> References, IReadOnlyList<SourceScanDiagnostic> Diagnostics);

/// <summary>Registered with existing DI, selected by IFrameworkFeatures.ScannerId.</summary>
public interface ISourceScanner
{
    string Id { get; }
    SourceFileResult Extract(SourceScanFile file);
}

public interface IProjectSourceCodeService
{
    Task<ProjectSourceScanResult> ScanAsync(SourceScanRequest request, CancellationToken cancellationToken = default);
    ProjectSourceScanResult? LastScan { get; }
    void Reset();
}
public sealed record ProjectSourceScanResult(IReadOnlyList<SourceReference> References, IReadOnlyList<SourceScanDiagnostic> Diagnostics,
    bool Complete, bool Cancelled, int FilesScanned, TimeSpan Duration)
{
    public bool CanClassifyUnused => Complete && !Cancelled && Diagnostics.Count == 0;
}
