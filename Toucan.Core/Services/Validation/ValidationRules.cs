using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Validation;

/// <summary>Keys present in primary language but missing (empty) in target languages.</summary>
public class MissingTranslationRule : IValidationRule
{
    public string Id => "missing-translation";
    public string Name => "Missing translations";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var primary = context.PrimaryLanguage;
        if (string.IsNullOrEmpty(primary)) yield break;

        var primaryKeys = context.Items
            .Where(i => i.Language == primary && !string.IsNullOrEmpty(i.Value))
            .Select(i => i.Namespace)
            .ToHashSet();

        var otherLangs = context.Items
            .Where(i => i.Language != primary)
            .GroupBy(i => i.Language);

        foreach (var lang in otherLangs)
        {
            var existing = lang.Where(i => !string.IsNullOrEmpty(i.Value)).Select(i => i.Namespace).ToHashSet();
            foreach (var key in primaryKeys.Except(existing))
                yield return new(Id, DefaultSeverity, $"Missing translation for '{key}'", key, lang.Key);
        }
    }
}

/// <summary>Placeholder count differs between primary and translated value.</summary>
public partial class PlaceholderMismatchRule : IValidationRule
{
    public string Id => "placeholder-mismatch";
    public string Name => "Placeholder mismatch";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var primary = context.PrimaryLanguage;
        if (string.IsNullOrEmpty(primary)) yield break;

        var sourceMap = context.Items
            .Where(i => i.Language == primary && !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, i => i.Value);

        foreach (var item in context.Items.Where(i => i.Language != primary && !string.IsNullOrEmpty(i.Value)))
        {
            if (!sourceMap.TryGetValue(item.Namespace, out var sourceVal)) continue;
            var srcPlaceholders = PlaceholderRegex().Count(sourceVal);
            var tgtPlaceholders = PlaceholderRegex().Count(item.Value);
            if (srcPlaceholders != tgtPlaceholders)
                yield return new(Id, DefaultSeverity, $"Placeholder count: source={srcPlaceholders}, target={tgtPlaceholders}", item.Namespace, item.Language);
        }
    }

    [GeneratedRegex(@"\{\{[\w.]+\}\}|\{[\w.]+\}|%[sd\d$]*|:[\w]+|\$\{[\w.]+\}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>Same namespace appears twice for the same language.</summary>
public class DuplicateKeyRule : IValidationRule
{
    public string Id => "duplicate-key";
    public string Name => "Duplicate keys";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var item in context.Items)
        {
            var key = (item.Namespace, item.Language);
            if (!seen.Add(key))
                yield return new(Id, DefaultSeverity, $"Duplicate key '{item.Namespace}'", item.Namespace, item.Language);
        }
    }
}

/// <summary>Translation value is identical to source (likely untranslated copy).</summary>
public class UntranslatedCopyRule : IValidationRule
{
    public string Id => "untranslated-copy";
    public string Name => "Untranslated (same as source)";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var primary = context.PrimaryLanguage;
        if (string.IsNullOrEmpty(primary)) yield break;

        var sourceMap = context.Items
            .Where(i => i.Language == primary && !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, i => i.Value);

        foreach (var item in context.Items.Where(i => i.Language != primary && !string.IsNullOrEmpty(i.Value)))
        {
            if (sourceMap.TryGetValue(item.Namespace, out var src) && item.Value == src)
                yield return new(Id, DefaultSeverity, "Translation identical to source", item.Namespace, item.Language);
        }
    }
}

/// <summary>Keys with empty string values.</summary>
public class EmptyValueRule : IValidationRule
{
    public string Id => "empty-value";
    public string Name => "Empty values";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var item in context.Items.Where(i => string.IsNullOrWhiteSpace(i.Value)))
            yield return new(Id, DefaultSeverity, "Empty translation value", item.Namespace, item.Language);
    }
}

/// <summary>Leading/trailing whitespace differences between source and target.</summary>
public class WhitespaceMismatchRule : IValidationRule
{
    public string Id => "whitespace-mismatch";
    public string Name => "Whitespace mismatch";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var primary = context.PrimaryLanguage;
        if (string.IsNullOrEmpty(primary)) yield break;

        var sourceMap = context.Items
            .Where(i => i.Language == primary && !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, i => i.Value);

        foreach (var item in context.Items.Where(i => i.Language != primary && !string.IsNullOrEmpty(i.Value)))
        {
            if (!sourceMap.TryGetValue(item.Namespace, out var src)) continue;
            var srcLeading = src.Length - src.TrimStart().Length;
            var srcTrailing = src.Length - src.TrimEnd().Length;
            var tgtLeading = item.Value.Length - item.Value.TrimStart().Length;
            var tgtTrailing = item.Value.Length - item.Value.TrimEnd().Length;
            if (srcLeading != tgtLeading || srcTrailing != tgtTrailing)
                yield return new(Id, DefaultSeverity, "Leading/trailing whitespace differs from source", item.Namespace, item.Language);
        }
    }
}
