namespace Toucan.Core.Models;

/// <summary>
/// How a format is recognized when scanning a folder with no project file. Lower <see cref="Priority"/> wins
/// when several formats match.
/// </summary>
public sealed record FormatDetection(int Priority, IReadOnlyList<string> Extensions, IReadOnlyList<string> FileNames);
