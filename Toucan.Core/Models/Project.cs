using System.IO;

namespace Toucan.Core.Models;

public class Project
{
    public required string Path { get; set; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) ?? Path;
    public DateTime LastOpened { get; set; }
    public bool IsValid() => Directory.Exists(Path);
}
