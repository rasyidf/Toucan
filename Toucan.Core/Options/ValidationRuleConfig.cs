namespace Toucan.Core.Options;

/// <summary>
/// Configuration for a single validation rule: whether it's enabled and its severity.
/// </summary>
public record ValidationRuleConfig(bool Enabled, string Severity);
