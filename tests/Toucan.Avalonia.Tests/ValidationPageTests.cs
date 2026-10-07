using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Services.Validation;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>The Settings → Validation page lists whatever rules are registered, not a fixed list.</summary>
public sealed class ValidationPageTests
{
    private static OptionsViewModel Options(IValidationPipeline? pipeline)
    {
        var host = new TestHost();
        return new OptionsViewModel(
            host.Services.GetRequiredService<IPreferenceService>(),
            host.Services.GetRequiredService<IProjectDefaultsService>(),
            host.Dialogs, new FakeMessageService(), validationPipeline: pipeline);
    }

    [AvaloniaFact]
    public void ListsEveryRegisteredRuleIncludingOnesFromPlugins()
    {
        var pipeline = new ValidationPipeline([new EmptyValueRule(), new PluginRule()]);

        var vm = Options(pipeline);

        Assert.Equal(["empty-value", "acme.max-length"], vm.ValidationRules.Select(r => r.Id));
        var plugin = vm.ValidationRules[1];
        Assert.Equal("Acme max length", plugin.Label);
        Assert.Equal("Error", plugin.Severity);
        Assert.True(plugin.Enabled);
    }

    [AvaloniaFact]
    public void TheShippedRulesKeepTheirLabelsAndDefaultSeverities()
    {
        var host = new TestHost();
        var vm = Options(host.Services.GetRequiredService<IValidationPipeline>());

        Assert.Equal(
            ["missing-translation:Missing translation:Warning", "placeholder-mismatch:Placeholder mismatch:Error", "duplicate-key:Duplicate key:Error",
             "untranslated-copy:Untranslated copy of source:Info", "empty-value:Empty value:Warning", "whitespace-mismatch:Leading/trailing whitespace mismatch:Info"],
            vm.ValidationRules.Select(r => $"{r.Id}:{r.Label}:{r.Severity}"));
    }

    [AvaloniaFact]
    public void WithoutAPipelineThePageIsEmptyRatherThanInventingRules()
    {
        Assert.Empty(Options(null).ValidationRules);
    }

    private sealed class PluginRule : IValidationRule
    {
        public string Id => "acme.max-length";
        public string Name => "Acme max length";
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;
        public IEnumerable<ValidationResult> Validate(ValidationContext context) => [];
    }
}
