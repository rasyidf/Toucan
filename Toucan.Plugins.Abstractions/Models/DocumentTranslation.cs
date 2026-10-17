namespace Toucan.Core.Models;

/// <summary>A structured path. Segments are literal names, never split on dots.</summary>
public sealed class TranslationPath : IEquatable<TranslationPath>
{
    public TranslationPath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var copy = segments.ToArray();
        if (copy.Any(s => s is null)) throw new ArgumentException("Path segments cannot be null.", nameof(segments));
        Segments = Array.AsReadOnly(copy);
    }

    public static TranslationPath Empty { get; } = new([]);
    public IReadOnlyList<string> Segments { get; }
    /// <summary>RFC 6901 pointer, including escaping literal slashes and tildes.</summary>
    public string EscapedPath => string.Concat(Segments.Select(s => "/" + s.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
    public bool Equals(TranslationPath? other) => other is not null && Segments.SequenceEqual(other.Segments, StringComparer.Ordinal);
    public override bool Equals(object? obj) => obj is TranslationPath other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Segments) hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>One parsed unit; source identity and language are assigned by the host.</summary>
public sealed record DocumentTranslation(TranslationPath KeyPath, string Value,
    string? RawScalar = null);

/// <summary>Explicit grouping of language documents belonging to the same logical resource.</summary>
/// <param name="Family">Stable, source-relative resource identity, excluding language. Null keeps documents separate.</param>
/// <param name="Namespace">Literal namespace segments, independent of the key path.</param>
public sealed record FrameworkDocumentLayout(string? Family, TranslationPath Namespace);
