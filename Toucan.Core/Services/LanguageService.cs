using System.ComponentModel;
using System.Globalization;

namespace Toucan.Core;

// ponytail: LanguageService is used by the WPF LanguagePromptViewModel for culture validation.
// When LanguagePrompt is fully ported to Avalonia, consider inlining this logic or moving to a shared helper.
internal class LanguageService
{
    public static readonly LanguageService Instance = new();

    private readonly List<LanguageModel> languages = [];

    public bool LanguageExists(string language) =>
        languages.Exists(l => l.Culture?.Name == language || l.Language == language);
}

public class LanguageModel : IDataErrorInfo
{
    public CultureInfo? Culture { get; set; }
    public string Language { get; set; } = string.Empty;

    public string this[string columnName]
    {
        get
        {
            if (columnName == "Language" && LanguageService.Instance.LanguageExists(Language))
                return "Language already exists";
            return null!;
        }
    }

    public string Error => null!;

    public override string ToString() => Culture?.NativeName ?? Language ?? base.ToString()!;
}
