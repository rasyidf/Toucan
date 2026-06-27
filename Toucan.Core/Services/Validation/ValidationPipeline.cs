using Toucan.Core.Contracts;

namespace Toucan.Core.Services.Validation;

public class ValidationPipeline(IEnumerable<IValidationRule> rules) : IValidationPipeline
{
    public IEnumerable<IValidationRule> Rules => rules;

    public IEnumerable<ValidationResult> RunAll(ValidationContext context)
        => rules.SelectMany(r => r.Validate(context));

    public IEnumerable<ValidationResult> Run(ValidationContext context, IEnumerable<string> ruleIds)
    {
        var ids = ruleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rules.Where(r => ids.Contains(r.Id)).SelectMany(r => r.Validate(context));
    }
}
