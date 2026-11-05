using Toucan.Core.Contracts;
using Toucan.Core.Services;

namespace Toucan.Core.Services.Validation;

public sealed class GlossaryRule : IValidationRule
{
    public string Id => "glossary";
    public string Name => "Project terminology";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (context is not ProjectValidationContext { Project: { } project }) yield break;
        var sources = context.Items.Where(i => i.Language == context.PrimaryLanguage).GroupBy(i => i.Namespace)
            .ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var item in context.Items.Where(i => i.Language != context.PrimaryLanguage && !string.IsNullOrEmpty(i.Value)))
        foreach (var entry in project.Glossary)
        {
            var translation = entry.Translations.GetValueOrDefault(item.Language);
            if (entry.Forbidden && (GlossaryService.ContainsTerm(item.Value, translation ?? entry.Term)))
                yield return new(Id, DefaultSeverity, $"Forbidden term '{translation ?? entry.Term}'. {entry.Description}", item.Namespace, item.Language);
            else if (!entry.Forbidden && !string.IsNullOrEmpty(translation) && sources.TryGetValue(item.Namespace, out var source)
                && GlossaryService.ContainsTerm(source, entry.Term) && !GlossaryService.ContainsTerm(item.Value, translation))
                yield return new(Id, DefaultSeverity, $"Use preferred term '{translation}' for '{entry.Term}'. {entry.Description}", item.Namespace, item.Language);
        }
    }
}
