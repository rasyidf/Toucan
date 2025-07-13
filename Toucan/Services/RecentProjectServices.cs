using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Toucan.Services;

internal interface IRecentFileService
{
    List<string> GetRecentPaths();
    void AddRecentPath(string path);
    void Save();
}

internal class RecentFileService : IRecentFileService
{
    private readonly string storageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Toucan", "recent.json");

    private readonly int maxItems = 10;
    private List<string> recentPaths = new();

    public RecentFileService()
    {
        if (File.Exists(storageFile))
        {
            string json = File.ReadAllText(storageFile);
            recentPaths = JsonConvert.DeserializeObject<List<string>>(json) ?? new();
        }
    }

    public List<string> GetRecentPaths() => recentPaths.ToList();

    public void AddRecentPath(string path)
    {
        recentPaths.Remove(path);
        recentPaths.Insert(0, path);
        if (recentPaths.Count > maxItems)
            recentPaths = recentPaths.Take(maxItems).ToList();
        Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageFile)!);
        File.WriteAllText(storageFile, JsonConvert.SerializeObject(recentPaths));
    }
}
