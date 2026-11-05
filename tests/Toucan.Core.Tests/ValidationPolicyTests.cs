using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Core.Services.Validation;
using Xunit;

namespace Toucan.Core.Tests;

public class ValidationPolicyTests
{
    [Fact]
    public void PartialProjectOverridesRetainDefaultsAndCanReenableARule()
    {
        var defaults = new ProjectDefaults { ValidationRules = new() {
            ["empty-value"] = new(false, "Error"), ["placeholder-mismatch"] = new(true, "Warning") } };
        var project = new ProjectSettings { ValidationRules = new() { ["empty-value"] = new(true, "Info") } };
        var pipeline = new ValidationPipeline([new EmptyValueRule(), new PlaceholderMismatchRule()]);
        var results = pipeline.RunAll(new ProjectValidationContext { Project = project, Defaults = defaults,
            PrimaryLanguage = "en", Items = [new() { Namespace = "a", Language = "en", Value = "Hi {name}" },
                new() { Namespace = "a", Language = "fr", Value = "Salut" }, new() { Namespace = "b", Language = "fr", Value = "" }] }).ToList();
        Assert.Contains(results, r => r.RuleId == "empty-value" && r.Severity == ValidationSeverity.Info);
        Assert.Contains(results, r => r.RuleId == "placeholder-mismatch" && r.Severity == ValidationSeverity.Warning);
        Assert.False(defaults.ValidationRules["empty-value"].Enabled);
    }

    [Theory]
    [InlineData(false, "Error", 0)]
    [InlineData(true, "Warning", 1)]
    [InlineData(true, "invalid", 1)]
    public void PolicyAppliesToExplicitRuns(bool enabled, string severity, int expected)
    {
        var pipeline = new ValidationPipeline([new EmptyValueRule()]);
        var results = pipeline.Run(new ProjectValidationContext { Project = new() { ValidationRules = new() {
            ["empty-value"] = new(enabled, severity) } }, Items = [new() { Language = "fr", Value = "" }] }, ["empty-value"]).ToList();
        Assert.Equal(expected, results.Count);
        if (expected > 0) Assert.Equal(ValidationSeverity.Warning, results[0].Severity);
    }

    [Fact]
    public void SettingsMergeIsCaseInsensitiveAndDoesNotMutateInputs()
    {
        var defaults = new Dictionary<string, ValidationRuleConfig> { ["empty-value"] = new(true, "Error") };
        var merged = EffectiveSettingsResolver.ResolveValidationRules(new() { ["EMPTY-VALUE"] = new(false, "Info") }, defaults);
        Assert.Single(merged);
        Assert.False(merged["empty-value"].Enabled);
        Assert.True(defaults["empty-value"].Enabled);
    }
}
