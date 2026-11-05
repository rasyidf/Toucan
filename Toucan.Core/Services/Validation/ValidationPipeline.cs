using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Core.Services.Validation;

public sealed class ProjectValidationContext : ValidationContext
{
    public ProjectSettings? Project { get; init; }
    public ProjectDefaults? Defaults { get; init; }
}

public class ValidationPipeline(IEnumerable<IValidationRule> rules, IProjectDefaultsService? defaults = null) : IValidationPipeline
{
    public IEnumerable<IValidationRule> Rules => rules;

    public IEnumerable<ValidationResult> RunAll(ValidationContext context) => Run(context, rules.Select(r => r.Id));

    public IEnumerable<ValidationResult> Run(ValidationContext context, IEnumerable<string> ruleIds)
    {
        var ids = ruleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var project = context as ProjectValidationContext;
        var policy = EffectiveSettingsResolver.ResolveValidationRules(project?.Project?.ValidationRules,
            (project?.Defaults ?? defaults?.Load() ?? new ProjectDefaults()).ValidationRules);
        foreach (var rule in rules.Where(r => ids.Contains(r.Id)))
        {
            var config = policy.GetValueOrDefault(rule.Id);
            if (config?.Enabled == false) continue;
            foreach (var finding in rule.Validate(context))
                yield return config is not null && Enum.TryParse<ValidationSeverity>(config.Severity, true, out var severity)
                    && Enum.IsDefined(severity) ? finding with { Severity = severity } : finding;
        }
    }
}
