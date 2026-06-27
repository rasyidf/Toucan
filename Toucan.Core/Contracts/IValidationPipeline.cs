using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public enum ValidationSeverity { Error, Warning, Info }

public record ValidationResult(string RuleId, ValidationSeverity Severity, string Message, string? Namespace = null, string? Language = null);

public class ValidationContext
{
    public required IEnumerable<TranslationItem> Items { get; init; }
    public required ProjectSettings Settings { get; init; }
    public string? PrimaryLanguage => Settings.PrimaryLanguage;
}

public interface IValidationRule
{
    string Id { get; }
    string Name { get; }
    ValidationSeverity DefaultSeverity { get; }
    IEnumerable<ValidationResult> Validate(ValidationContext context);
}

public interface IValidationPipeline
{
    IEnumerable<ValidationResult> RunAll(ValidationContext context);
    IEnumerable<ValidationResult> Run(ValidationContext context, IEnumerable<string> ruleIds);
    IEnumerable<IValidationRule> Rules { get; }
}
