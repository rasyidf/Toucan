using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IRecentProjectService
{
    List<Project> LoadRecent();
    void Add(string projectPath);
    void Remove(string projectPath);
    void Save();
}
