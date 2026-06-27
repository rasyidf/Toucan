using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Services;

internal class RecentProjectService : IRecentProjectService
{
    private const int MaxItems = 10;
    private readonly string storageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Toucan", "recent_projects.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
        Project? existing = recentProjects.FirstOrDefault(p => p.Path == projectPath);
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
        _ = recentProjects.RemoveAll(p => p.Path == projectPath);
        Save();
    }

    public void Save()
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(storageFile)!);
        File.WriteAllText(storageFile, JsonSerializer.Serialize(recentProjects, JsonOptions));
    }

    private void Load()
    {
        if (File.Exists(storageFile))
        {
            try
            {
                string json = File.ReadAllText(storageFile);
                recentProjects = JsonSerializer.Deserialize<List<Project>>(json, JsonOptions) ?? [];
            }
            catch
            {
                recentProjects = [];
            }
        }
    }
}
