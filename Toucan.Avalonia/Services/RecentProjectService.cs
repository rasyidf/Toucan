using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>Most-recently-used project folders, shared with the WPF app's storage file.</summary>
public sealed class RecentProjectService : IRecentProjectService
{
    private const int MaxItems = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storageFile;
    private List<Project> _recentProjects = [];

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
        _recentProjects = _recentProjects
            .Where(p => p.IsValid())
            .OrderByDescending(p => p.LastOpened)
            .Take(MaxItems)
            .ToList();
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

        _recentProjects = _recentProjects
            .DistinctBy(p => p.Path)
            .OrderByDescending(p => p.LastOpened)
            .Take(MaxItems)
            .ToList();
        Save();
    }

    public void Remove(string projectPath)
    {
        _recentProjects.RemoveAll(p => p.Path == projectPath);
        Save();
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
