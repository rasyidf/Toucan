using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>Most-recently-used project folders, shared with the WPF app's storage file.</summary>
public sealed class RecentProjectService : IRecentProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storageFile;
    private List<Project> _recentProjects = [];
    private int _limit = 10;

    public int Limit
    {
        get => _limit;
        set => _limit = Math.Max(1, value);
    }

    public RecentProjectService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toucan", "recent_projects.json"))
    {
    }

    internal RecentProjectService(string storageFile)
    {
        _storageFile = storageFile;
        Load();
    }

    public List<Project> LoadRecent()
    {
        // Pinned projects survive a missing folder (an unplugged drive); the rest are dropped when their folder is gone.
        _recentProjects = Normalize(_recentProjects.Where(p => p.IsPinned || p.IsValid()));
        Save();
        return _recentProjects;
    }

    public void Add(string projectPath)
    {
        var existing = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
        if (existing != null)
            existing.LastOpened = DateTime.Now;
        else
            _recentProjects.Insert(0, new Project { Path = projectPath, LastOpened = DateTime.Now });

        _recentProjects = Normalize(_recentProjects);
        Save();
    }

    public void Remove(string projectPath)
    {
        _recentProjects.RemoveAll(p => p.Path == projectPath);
        Save();
    }

    public void SetPinned(string projectPath, bool pinned)
    {
        var project = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
        if (project == null) return;
        project.IsPinned = pinned;
        _recentProjects = Normalize(_recentProjects);
        Save();
    }

    public void SetPrimaryLanguage(string projectPath, string language)
    {
        var project = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
        if (project == null || string.IsNullOrWhiteSpace(language) || project.PrimaryLanguage == language) return;
        project.PrimaryLanguage = language;
        Save();
    }

    public void Clear(bool keepPinned)
    {
        _recentProjects = keepPinned ? _recentProjects.Where(p => p.IsPinned).ToList() : [];
        Save();
    }

    /// <summary>Pinned first, newest first within each group; only unpinned entries count against <see cref="Limit"/>.</summary>
    private List<Project> Normalize(IEnumerable<Project> projects)
    {
        var distinct = projects.DistinctBy(p => p.Path).ToList();
        var pinned = distinct.Where(p => p.IsPinned).OrderByDescending(p => p.LastOpened);
        var rest = distinct.Where(p => !p.IsPinned).OrderByDescending(p => p.LastOpened).Take(Limit);
        return pinned.Concat(rest).ToList();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storageFile)!);
            File.WriteAllText(_storageFile, JsonSerializer.Serialize(_recentProjects, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recent list is a convenience; failing to persist it must not break project loading.
        }
    }

    private void Load()
    {
        if (!File.Exists(_storageFile)) return;
        try
        {
            _recentProjects = JsonSerializer.Deserialize<List<Project>>(File.ReadAllText(_storageFile), JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _recentProjects = [];
        }
    }
}
