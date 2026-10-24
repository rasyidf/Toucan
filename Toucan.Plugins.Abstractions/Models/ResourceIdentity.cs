using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Core.Models;

/// <summary>Property names and array indices are different kinds, even when both display as numbers.</summary>
public sealed record ResourcePathSegment(string? Property = null, int? Index = null)
{
    public static ResourcePathSegment Named(string name) => new(name);
    public static ResourcePathSegment At(int index) => index >= 0 ? new(Index: index) : throw new ArgumentOutOfRangeException(nameof(index));
}

public sealed record ResourceEntryIdentity
{
    public string EntryId { get; init; } = Guid.NewGuid().ToString("N");
    public string ResourceId { get; init; } = string.Empty;
    public string Package { get; init; } = "main";
    public IReadOnlyList<ResourcePathSegment> Path { get; init; } = [];
    /// <summary>Compatibility selector for string-key callers; never used to serialize a resource key.</summary>
    public string Selector { get; init; } = string.Empty;
    public string PackagePrefix { get; init; } = string.Empty;
    [JsonIgnore] public string KeyToken => JsonSerializer.Serialize(Path);
    [JsonIgnore] public string Address => JsonSerializer.Serialize(new { ResourceId, Path });
}

/// <summary>All existing key setters pass through this boundary, including old plugins.</summary>
public static class ResourceEntryOperations
{
    public static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(".", "\\.", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    public static IReadOnlyList<string> Hierarchy(string selector)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            if (selector[i] == '\\') { i++; continue; }
            if (selector[i] == '.') { parts.Add(selector[start..i]); start = i + 1; }
        }
        parts.Add(selector[start..]);
        return parts;
    }

    public static string ParentPrefix(string selector)
    {
        var parts = Hierarchy(selector);
        return parts.Count <= 1 ? "" : string.Join('.', parts.Take(parts.Count - 1)) + ".";
    }

    public static string DisplaySegment(string selector)
    {
        var value = new System.Text.StringBuilder();
        for (var i = 0; i < selector.Length; i++) value.Append(selector[i] == '\\' && i + 1 < selector.Length ? selector[++i] : selector[i]);
        return value.ToString();
    }

    public static string Selector(IEnumerable<ResourcePathSegment> path, bool escaped = false)
    {
        var result = string.Empty;
        foreach (var part in path)
            result += part.Index is { } index ? $"[{index}]" : (result.Length == 0 ? "" : ".") + (escaped ? Escape(part.Property!) : part.Property);
        return result;
    }

    public static ResourcePathSegment[] ParseSelector(string selector)
    {
        var parts = new List<ResourcePathSegment>();
        var name = new System.Text.StringBuilder();
        for (var i = 0; i < selector.Length; i++)
        {
            var c = selector[i];
            if (c == '\\' && i + 1 < selector.Length) { name.Append(selector[++i]); continue; }
            if (c == '.') { if (name.Length > 0) { parts.Add(ResourcePathSegment.Named(name.ToString())); name.Clear(); } continue; }
            if (c == '[' && selector.IndexOf(']', i) is var close && close > i && int.TryParse(selector.AsSpan(i + 1, close - i - 1), out var index) && index >= 0)
            {
                if (name.Length > 0) { parts.Add(ResourcePathSegment.Named(name.ToString())); name.Clear(); }
                parts.Add(ResourcePathSegment.At(index)); i = close; continue;
            }
            name.Append(c);
        }
        if (name.Length > 0) parts.Add(ResourcePathSegment.Named(name.ToString()));
        return parts.ToArray();
    }

    public static ResourceEntryIdentity Rename(ResourceEntryIdentity identity, string selector)
    {
        var key = selector.StartsWith(identity.PackagePrefix, StringComparison.Ordinal) ? selector[identity.PackagePrefix.Length..] : selector;
        // A single literal property stays literal when its legacy selector contains dots.
        var literal = new System.Text.StringBuilder();
        for (var i = 0; i < key.Length; i++) literal.Append(key[i] == '\\' && i + 1 < key.Length ? key[++i] : key[i]);
        var path = identity.Path is [{ Property: not null }]
            ? new[] { ResourcePathSegment.Named(literal.ToString()) } : ParseSelector(key);
        return identity with { Path = path, Selector = selector };
    }

    public static TranslationItem Copy(TranslationItem item, bool duplicate = false)
    {
        return new TranslationItem
        {
            Language = item.Language, Namespace = item.Namespace, Value = item.Value, Comment = item.Comment, IsApproved = item.IsApproved,
            Identity = item.Identity is { } identity ? identity with { Path = [.. identity.Path], EntryId = duplicate ? Guid.NewGuid().ToString("N") : identity.EntryId } : null,
            FormatData = item.FormatData is null ? null : new(item.FormatData), LastModifiedUtc = item.LastModifiedUtc,
            ApprovedAtUtc = item.ApprovedAtUtc, ChangeType = item.ChangeType, Permissions = item.Permissions
        };
    }

    public static void Move(TranslationItem item, ResourceDescriptor destination, string selector)
    {
        if (!item.Permissions.EditKeys) return;
        var identity = item.Identity ?? new ResourceEntryIdentity();
        var prefix = destination.SelectorPrefix;
        item.Identity = identity with { ResourceId = destination.Id, Package = destination.Package, PackagePrefix = prefix,
            Path = destination.Options.Layout is ResourceLayout.Literal or ResourceLayout.Dotted || destination.FormatId is FormatIds.Arb or FormatIds.JavaProperties or FormatIds.Resx
                ? [ResourcePathSegment.Named(selector)] : ParseSelector(selector), Selector = prefix + selector };
        item.Namespace = prefix + selector;
        item.Language = destination.Language;
        if (destination.FormatId == FormatIds.Json) (item.FormatData ??= new())["json.file"] = destination.RelativePath;
    }

    public static void RenameKey(TranslationItem item, IReadOnlyList<ResourcePathSegment> path)
    {
        if (!item.Permissions.EditKeys) return;
        var identity = item.Identity ?? throw new InvalidOperationException("This entry has no resource identity.");
        var selector = identity.PackagePrefix + Selector(path, escaped: true);
        item.Identity = identity with { Path = path.ToArray(), Selector = selector };
        item.Namespace = selector;
    }
}
