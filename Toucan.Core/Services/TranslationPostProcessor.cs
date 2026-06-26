using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Toucan.Core.Services;

/// <summary>
/// Post-processes translation results: preserves parameters, keeps uppercase first letter.
/// ponytail: regex-based placeholder protection. Upgrade path: per-framework param style config.
/// </summary>
public static partial class TranslationPostProcessor
{
    // Matches: {{var}}, {0}, {name}, %s, %d, %1$s, :param, ${var}, {t('key')}
    private static readonly Regex s_paramPattern = ParamRegex();

    [GeneratedRegex(@"\{\{[\w.]+\}\}|\{[\w.]+\}|%[sd\d$]*|:[\w]+|\$\{[\w.]+\}", RegexOptions.Compiled)]
    private static partial Regex ParamRegex();

    /// <summary>
    /// Protects parameters in source text before translation.
    /// Returns the cleaned text and a list of placeholders to restore.
    /// </summary>
    public static (string CleanedText, List<string> Placeholders) ProtectParameters(string sourceText)
    {
        var placeholders = new List<string>();
        var cleaned = s_paramPattern.Replace(sourceText, m =>
        {
            placeholders.Add(m.Value);
            return $"⟨{placeholders.Count - 1}⟩";
        });
        return (cleaned, placeholders);
    }

    /// <summary>Restores placeholders in translated text.</summary>
    public static string RestoreParameters(string translatedText, List<string> placeholders)
    {
        if (placeholders == null || placeholders.Count == 0) return translatedText;
        for (int i = 0; i < placeholders.Count; i++)
            translatedText = translatedText.Replace($"⟨{i}⟩", placeholders[i]);
        return translatedText;
    }

    /// <summary>If source starts with uppercase, ensure result does too.</summary>
    public static string PreserveUppercaseFirst(string source, string translated)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translated)) return translated;
        if (char.IsUpper(source[0]) && char.IsLower(translated[0]))
            return char.ToUpper(translated[0]) + translated[1..];
        return translated;
    }

    /// <summary>Full post-processing pipeline for a single translation.</summary>
    public static string Process(string sourceText, string translatedText)
    {
        if (string.IsNullOrEmpty(translatedText)) return translatedText;
        translatedText = PreserveUppercaseFirst(sourceText, translatedText);
        return translatedText;
    }
}
