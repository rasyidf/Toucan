using System.IO;

namespace Toucan.Core.Models;

/// <summary>
/// Saving would overwrite files with constructs the format cannot write back (see
/// <see cref="FormatSupport.Unsupported"/>). Derives from <see cref="IOException"/> so existing save error handling shows it.
/// </summary>
public sealed class FormatSaveBlockedException : IOException
{
    public FormatSaveBlockedException(string formatId, IReadOnlyList<string> constructs)
        : base($"Saving was blocked to protect your files. The {formatId} files contain content Toucan cannot write back, "
               + $"and saving would remove it: {string.Join("; ", constructs)}. Edit those files outside Toucan, or use Save As to write a copy.")
    {
        FormatId = formatId;
        Constructs = constructs;
    }

    public FormatSaveBlockedException()
        : this("unknown", [])
    { }

    public FormatSaveBlockedException(string message)
        : base(message)
    {
        FormatId = "unknown";
        Constructs = [];
    }

    public FormatSaveBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
        FormatId = "unknown";
        Constructs = [];
    }

    public string FormatId { get; }
    public IReadOnlyList<string> Constructs { get; }
}
