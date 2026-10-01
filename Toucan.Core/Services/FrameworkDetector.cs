using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Detects a built-in format from folder structure. Kept for callers without a container; app code should
/// use <see cref="FormatDetector"/> so plugin formats are considered.
/// </summary>
public static class FrameworkDetector
{
    private static readonly FormatDetector s_builtIn = new(BuiltInFormats.SaveStrategies);

    public static SaveStyles Detect(string folder) =>
        FormatIds.TryGetStyle(s_builtIn.Detect(folder), out var style) ? style : SaveStyles.Json;
}
