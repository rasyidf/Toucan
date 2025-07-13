using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Services;

internal class RecentProjectService : IRecentProjectService
{
    private const int MaxItems = 10;
    private readonly string storageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Toucan", "recent_projects.json");

    private List<Project> recentProjects = [];

    public RecentProjectService()
    {
        Load();
    }

    public List<Project> LoadRecent()
    {
        // Clean invalid ones on load
        recentProjects = recentProjects
            .Where(p => p.IsValid())
            .OrderByDescending(p => p.LastOpened)
            .Take(MaxItems)
            .ToList();

        Save();
        return recentProjects;
    }

    public void Add(string projectPath)
    {
        var existing = recentProjects.FirstOrDefault(p => p.Path == projectPath);
        if (existing != null)
        {
            existing.LastOpened = DateTime.Now;
        }
        else
        {
            var newProj = new Project
            {
                Path = projectPath,
                LastOpened = DateTime.Now
            };
            recentProjects.Insert(0, newProj);
        }

        recentProjects = recentProjects
            .DistinctBy(p => p.Path)
            .OrderByDescending(p => p.LastOpened)
            .Take(MaxItems)
            .ToList();

        Save();
    }

    public void Remove(string projectPath)
    {
        recentProjects.RemoveAll(p => p.Path == projectPath);
        Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageFile)!);
        File.WriteAllText(storageFile, JsonConvert.SerializeObject(recentProjects, Formatting.Indented));
    }

    private void Load()
    {
        if (File.Exists(storageFile))
        {
            try
            {
                string json = File.ReadAllText(storageFile);
                recentProjects = JsonConvert.DeserializeObject<List<Project>>(json) ?? [];
            }
            catch
            {
                recentProjects = [];
            }
        }
    }
}
