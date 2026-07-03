namespace Toucan.Core.Models;

/// <summary>Persisted definition for a user-configured validation rule.</summary>
public class CustomRuleDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty; // MaxLength, ForbiddenWords, Regex, Required
    public string Severity { get; set; } = "Warning";    // Error, Warning, Info
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Params { get; set; } = [];
}
