using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Optional migration provider. Reads user-owned projects; never writes the source format.</summary>
public interface IProjectCompatibilityImporter
{
    IEnumerable<string> FindProjects(string folder);
    CompatibilityProject Read(string file);
    void ApplyMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items);
    void CaptureMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items);
}

public sealed class CompatibilityImportException : FormatException
{
    public CompatibilityImportException() : this(Array.Empty<ResourceDiagnostic>()) { }
    public CompatibilityImportException(string message) : base(message) { Diagnostics = []; }
    public CompatibilityImportException(string message, Exception innerException) : base(message, innerException) { Diagnostics = []; }
    public CompatibilityImportException(IReadOnlyList<ResourceDiagnostic> diagnostics)
        : base(string.Join("; ", diagnostics.Select(d => $"{d.RelativePath} [{d.Field}]: {d.Message}"))) { Diagnostics = diagnostics; }
    public IReadOnlyList<ResourceDiagnostic> Diagnostics { get; }
}
