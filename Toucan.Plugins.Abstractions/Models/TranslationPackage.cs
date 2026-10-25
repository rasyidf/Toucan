namespace Toucan.Core.Models;

/// <summary>A named package of translation files within a project.</summary>
public class TranslationPackage
{
    public string Name { get; set; } = "main";
    public List<TranslationUrl> TranslationUrls { get; set; } = [];
}

public class TranslationUrl
{
    public string Language { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
