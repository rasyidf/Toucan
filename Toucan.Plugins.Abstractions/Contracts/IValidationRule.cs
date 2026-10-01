using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public enum ValidationSeverity { Error, Warning, Info }

public record ValidationResult(string RuleId, ValidationSeverity Severity, string Message, string? Namespace = null, string? Language = null, string? SuggestedFix = null);

public class ValidationContext
{
    public required IEnumerable<TranslationItem> Items { get; init; }

    /// <summary>The project's primary (source) language code, or null if none is set.</summary>
    public string? PrimaryLanguage { get; init; }
}

public interface IValidationRule
{
    string Id { get; }
    string Name { get; }
    ValidationSeverity DefaultSeverity { get; }
    IEnumerable<ValidationResult> Validate(ValidationContext context);
}
