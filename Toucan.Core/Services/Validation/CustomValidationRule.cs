using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Validation;

/// <summary>Types of configurable custom validation rules.</summary>
public enum CustomRuleType { MaxLength, ForbiddenWords, Regex, Required }

/// <summary>
/// A user-configurable validation rule instantiated from <see cref="CustomRuleDefinition"/>.
/// </summary>
public class CustomValidationRule : IValidationRule
{
    private readonly CustomRuleDefinition _definition;
    private readonly CustomRuleType _type;

    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public ValidationSeverity DefaultSeverity { get; }

    public CustomValidationRule(CustomRuleDefinition definition)
    {
        _definition = definition;
        _type = Enum.TryParse<CustomRuleType>(definition.RuleType, ignoreCase: true, out var t) ? t : CustomRuleType.Required;
        DefaultSeverity = Enum.TryParse<ValidationSeverity>(definition.Severity, ignoreCase: true, out var s) ? s : ValidationSeverity.Warning;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!_definition.Enabled) yield break;

        foreach (var item in context.Items)
        {
            var result = _type switch
            {
                CustomRuleType.MaxLength => CheckMaxLength(item),
                CustomRuleType.ForbiddenWords => CheckForbiddenWords(item),
                CustomRuleType.Regex => CheckRegex(item),
                CustomRuleType.Required => CheckRequired(item),
                _ => null
            };
            if (result != null) yield return result;
        }
    }

    private ValidationResult? CheckMaxLength(TranslationItem item)
    {
        if (string.IsNullOrEmpty(item.Value)) return null;
        if (!_definition.Params.TryGetValue("maxLength", out var raw) || !int.TryParse(raw, out var max)) return null;
        if (item.Value.Length <= max) return null;

        var suggested = item.Value[..max];
        return new(Id, DefaultSeverity, $"Value exceeds max length {max} (actual: {item.Value.Length})", item.Namespace, item.Language, suggested);
    }

    private ValidationResult? CheckForbiddenWords(TranslationItem item)
    {
        if (string.IsNullOrEmpty(item.Value)) return null;
        if (!_definition.Params.TryGetValue("words", out var wordList)) return null;

        var words = wordList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = words.FirstOrDefault(w => item.Value.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (found == null) return null;

        return new(Id, DefaultSeverity, $"Contains forbidden word '{found}'", item.Namespace, item.Language);
    }

    private ValidationResult? CheckRegex(TranslationItem item)
    {
        if (string.IsNullOrEmpty(item.Value)) return null;
        if (!_definition.Params.TryGetValue("pattern", out var pattern)) return null;

        var mustMatch = !_definition.Params.TryGetValue("mustMatch", out var mm) || !bool.TryParse(mm, out var b) || b;

        try
        {
            var matches = Regex.IsMatch(item.Value, pattern);
            if (mustMatch && !matches)
                return new(Id, DefaultSeverity, $"Value does not match required pattern", item.Namespace, item.Language);
            if (!mustMatch && matches)
                return new(Id, DefaultSeverity, $"Value matches forbidden pattern", item.Namespace, item.Language);
        }
        catch (RegexParseException) { /* ponytail: skip invalid user regex silently; upgrade: surface config error in UI */ }

        return null;
    }

    private ValidationResult? CheckRequired(TranslationItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Value)) return null;
        return new(Id, DefaultSeverity, "Required value is missing", item.Namespace, item.Language);
    }
}
