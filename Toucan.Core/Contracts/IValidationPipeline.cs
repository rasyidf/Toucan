using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IValidationPipeline
{
    IEnumerable<ValidationResult> RunAll(ValidationContext context);
    IEnumerable<ValidationResult> Run(ValidationContext context, IEnumerable<string> ruleIds);
    IEnumerable<IValidationRule> Rules { get; }
}
