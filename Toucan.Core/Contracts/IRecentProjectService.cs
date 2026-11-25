using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IRecentProjectService
{
    /// <summary>How many unpinned projects the list keeps. Pinned projects do not count against it.</summary>
    int Limit { get; set; }

    /// <summary>Pinned projects first, then the rest by most recently opened.</summary>
    List<Project> LoadRecent();
    void Add(string projectPath);
    void Remove(string projectPath);
    void SetPinned(string projectPath, bool pinned);
    void SetPrimaryLanguage(string projectPath, string language);

    /// <summary>Shows <paramref name="label"/> in the recent list instead of the folder name; null clears it.</summary>
    void SetLabel(string projectPath, string? label) { }

    /// <summary>Empties the list; with <paramref name="keepPinned"/> the pinned projects stay.</summary>
    void Clear(bool keepPinned);
    void Save();
}
