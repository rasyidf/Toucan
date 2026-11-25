using System.Text;
using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>A proposed pairing of a project language with a remote one. Only exact matches are applied without asking.</summary>
public sealed record LanguageSuggestion(string ProjectLanguage, string? RemoteLanguage, bool IsExact);

/// <summary>
/// Turns remote key names into path segments and back. Segments are joined with the separator; a separator or backslash
/// inside a segment is escaped with a backslash, so every path has exactly one name and every name exactly one path.
/// </summary>
public sealed class SourceKeyMapper
{
    private readonly string _separator;

    public SourceKeyMapper(SourceKeyMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrEmpty(mapping.Separator)) throw new ArgumentException("The key separator cannot be empty.", nameof(mapping));
        if (mapping.Separator.Contains('\\', StringComparison.Ordinal)) throw new ArgumentException("The key separator cannot contain a backslash.", nameof(mapping));
        _separator = mapping.Separator;
        Comparer = mapping.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    /// <summary>How the service compares key names.</summary>
    public StringComparer Comparer { get; }

    public string ToRemote(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return string.Join(_separator, segments.Select(s => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(_separator, "\\" + _separator, StringComparison.Ordinal)));
    }

    public string ToRemote(TranslationPath @namespace, TranslationPath keyPath)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(keyPath);
        return ToRemote([.. @namespace.Segments, .. keyPath.Segments]);
    }

    public IReadOnlyList<string> FromRemote(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var segments = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length;)
        {
            if (name[i] == '\\' && i + 1 < name.Length)
            {
                if (string.CompareOrdinal(name, i + 1, _separator, 0, _separator.Length) == 0) { current.Append(_separator); i += 1 + _separator.Length; continue; }
                if (name[i + 1] == '\\') { current.Append('\\'); i += 2; continue; }
            }
            if (string.CompareOrdinal(name, i, _separator, 0, _separator.Length) == 0)
            {
                segments.Add(current.ToString());
                current.Clear();
                i += _separator.Length;
                continue;
            }
            current.Append(name[i]);
            i++;
        }
        segments.Add(current.ToString());
        return segments;
    }

    /// <summary>Groups of project paths that would become the same remote key; each group is a collision.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Collisions(IEnumerable<IReadOnlyList<string>> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return [.. paths.Select(ToRemote).Distinct(StringComparer.Ordinal).GroupBy(n => n, Comparer).Where(g => g.Count() > 1).Select(g => (IReadOnlyList<string>)[.. g])];
    }
}

/// <summary>Checks and suggests a connection's language and resource mapping.</summary>
public static class SourceMapping
{
    /// <summary>
    /// For each project language: an exact match (same tag ignoring case and '_' versus '-'), else a remote language with
    /// the same primary language when there is only one, marked inexact so the user confirms it, else none.
    /// </summary>
    public static IReadOnlyList<LanguageSuggestion> SuggestLanguages(IEnumerable<string> projectLanguages, IEnumerable<RemoteLanguage> remote)
    {
        ArgumentNullException.ThrowIfNull(projectLanguages);
        ArgumentNullException.ThrowIfNull(remote);
        var remoteCodes = remote.Select(r => r.Code).ToList();
        var result = new List<LanguageSuggestion>();
        foreach (var language in projectLanguages)
        {
            var exact = remoteCodes.Find(c => Normalize(c) == Normalize(language));
            if (exact is not null) { result.Add(new(language, exact, true)); continue; }
            var sameBase = remoteCodes.Where(c => Primary(c) == Primary(language)).ToList();
            result.Add(sameBase.Count == 1 ? new(language, sameBase[0], false) : new(language, null, false));
        }
        return result;
    }

    /// <summary>Every problem with the connection's mapping, checked against the remote project when it is known.</summary>
    public static IReadOnlyList<string> Validate(SourceConnectionSettings connection, IReadOnlyCollection<string> projectLanguages, SourceProjectInfo? remote = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(projectLanguages);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(connection.Id)) problems.Add("The connection needs an ID.");
        if (connection.Role is not (SourceRoles.Mirror or SourceRoles.Primary)) problems.Add($"Role '{connection.Role}' is not mirror or primary.");
        if (connection.Policy.DeleteRemote is not ("ask" or "never" or "always")) problems.Add($"Remote deletion policy '{connection.Policy.DeleteRemote}' is not ask, never or always.");
        if (connection.BaseUrl is { } url && !SourceAdapterRules.IsSecure(url))
            problems.Add($"The address {url} must be an absolute HTTPS address.");

        try { _ = new SourceKeyMapper(connection.Keys); }
        catch (ArgumentException ex) { problems.Add(ex.Message); }

        foreach (var language in connection.Languages.Keys.Where(l => !projectLanguages.Contains(l, StringComparer.OrdinalIgnoreCase)))
            problems.Add($"Language {language} is mapped but is not in the project.");
        foreach (var group in connection.Languages.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"Remote language {group.Key} is mapped from {string.Join(" and ", group.Select(p => p.Key))}; map each remote language once.");
        foreach (var group in connection.Resources.GroupBy(r => r.Remote, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Remote resource {group.Key} is mapped more than once.");
        if (connection.Resources.Any(r => string.IsNullOrWhiteSpace(r.Remote))) problems.Add("A resource mapping has no remote resource.");

        if (remote is not null)
        {
            var codes = remote.Languages.Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (language, code) in connection.Languages.Where(p => !codes.Contains(p.Value)))
                problems.Add($"Language {language} is mapped to {code}, which the remote project does not have.");
            var resources = remote.Resources.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var map in connection.Resources.Where(r => !resources.Contains(r.Remote)))
                problems.Add($"Remote resource {map.Remote} does not exist in the remote project.");
            if (connection.RemoteProject is { } project && project.Id != remote.RemoteProjectId)
                problems.Add($"The connection is for remote project {project.Id}, but the service described {remote.RemoteProjectId}.");
        }
        return problems;
    }

    /// <summary>Project languages that will not be read or written because they have no remote mapping.</summary>
    public static IReadOnlyList<string> Unmapped(SourceConnectionSettings connection, IEnumerable<string> projectLanguages)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return [.. projectLanguages.Where(l => !connection.Languages.ContainsKey(l))];
    }

    private static string Normalize(string tag) => tag.Replace('_', '-').ToLowerInvariant();
    private static string Primary(string tag) => Normalize(tag).Split('-')[0];
}
