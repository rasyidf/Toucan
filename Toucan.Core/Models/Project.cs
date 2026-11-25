using System.IO;

namespace Toucan.Core.Models;

public class Project
{
    public required string Path { get; set; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) ?? Path;

    /// <summary>
    /// A name to show instead of the folder's, for a project that was opened from a service (<c>crowdin:my-project</c>).
    /// </summary>
    public string? Label { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label;
    public DateTime LastOpened { get; set; }

    /// <summary>Pinned projects stay at the top of the recent list and are never pushed out by newer ones.</summary>
    public bool IsPinned { get; set; }

    /// <summary>The source language the project used the last time it was open; feeds preferred-language detection.</summary>
    public string? PrimaryLanguage { get; set; }
    public bool IsValid() => Directory.Exists(Path);
}
