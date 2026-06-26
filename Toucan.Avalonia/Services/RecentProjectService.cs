using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

public class RecentProjectService : IRecentProjectService
{
    private const int MaxItems = 10;
    private readonly string _storageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Toucan", "recent_projects.json");

    private List<Project> _recentProjects = [];

    public RecentProjectService() => Load();

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
        Directory.CreateDirectory(Path.GetDirectoryName(_storageFile)!);
        File.WriteAllText(_storageFile, JsonConvert.SerializeObject(_recentProjects, Formatting.Indented));
    }

    private void Load()
    {
        if (!File.Exists(_storageFile)) return;
        try
        {
            _recentProjects = JsonConvert.DeserializeObject<List<Project>>(File.ReadAllText(_storageFile)) ?? [];
        }
        catch { _recentProjects = []; }
    }
}
