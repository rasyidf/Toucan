using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.Validation;
using Xunit;

namespace Toucan.Core.Tests;

public class TerminologyAndMessageTests
{
    [Fact]
    public void GlossaryCsvRetainsQuotedMultilineDescriptionsAndLanguages()
    {
        var entries = new List<GlossaryEntry> { new() { Term = "account", Description = "A \"bank\",\naccount", Translations = new() { ["fr"] = "compte", ["id"] = "rekening" } }, new() { Term = "blacklist", Forbidden = true } };
        var restored = GlossaryService.ImportCsv(GlossaryService.ExportCsv(entries));
        Assert.Equal(entries[0].Description, restored[0].Description);
        Assert.Equal("rekening", restored[0].Translations["id"]);
        Assert.True(restored[1].Forbidden);
    }

    [Theory]
    [InlineData("term,description,forbidden,language,translation\n\"oops", "Unclosed")]
    [InlineData("wrong,header", "columns")]
    [InlineData("term,description,forbidden,language,translation\nterm,,false,fr,a\nterm,,false,fr,b", "Duplicate")]
    public void InvalidCsvFailsBeforeChangingTheProject(string csv, string message) => Assert.Contains(message, Assert.Throws<FormatException>(() => GlossaryService.ImportCsv(csv)).Message);

    [Fact]
    public void GlossaryDistinguishesWholeTermsAndEnforcesTargetVocabulary()
    {
        var project = new ProjectSettings { Glossary = [new() { Term = "account", Translations = new() { ["fr"] = "compte" } }, new() { Term = "blacklist", Forbidden = true }] };
        Assert.Empty(GlossaryService.Matches(project.Glossary, "accounting"));
        Assert.Single(GlossaryService.Matches(project.Glossary, "Account balance"));
        var findings = new GlossaryRule().Validate(new ProjectValidationContext { Project = project, PrimaryLanguage = "en", Items = [
            new() { Namespace = "a", Language = "en", Value = "Account" }, new() { Namespace = "a", Language = "fr", Value = "blacklist" }] }).ToList();
        Assert.Equal(2, findings.Count);
    }

    [Theory]
    [InlineData("{n, plural, one {One} other {# things}}")]
    [InlineData("{g, select, male {{n, plural, offset:1 =0 {None} other {{name} and # more}}} other {Hi {name}}}")]
    [InlineData("'{literal}' {n, selectordinal, one {#st} other {#th}}")]
    [InlineData("Don't forget {n, plural, one {{date, date, short}} other {'{braces}'}}")]
    public void NestedMessagesAndQuotedSyntaxAreValid(string text) => Assert.Null(MessageSyntaxService.Parse(text).Error);

    [Theory]
    [InlineData("{n, plural, one {One}}", "other")]
    [InlineData("{n, plural, other {One} other {Two}}", "Duplicate")]
    [InlineData("{n, plural, bananas {One} other {Two}}", "selector")]
    [InlineData("{n, plural, other {Unclosed}", "Unclosed")]
    [InlineData("{n, choice, 0#None}", "Unsupported")]
    public void InvalidMessagesExplainTheFailure(string text, string message) => Assert.Contains(message, MessageSyntaxService.Parse(text).Error!);

    [Fact]
    public void PluralCategoriesMayChangeButSelectBranchesAndArgumentsRemainRequired()
    {
        const string source = "{g, select, male {{n, plural, one {{name}} other {{name}}}} other {{name}}}";
        var valid = MessageFindings(source, "{g, select, male {{n, plural, few {{name}} many {{name}} other {{name}}}} other {{name}}}");
        Assert.Empty(valid);
        var invalid = MessageFindings(source, "{g, select, other {{wrong}}}");
        Assert.Contains(invalid, f => f.Message.Contains("male", StringComparison.Ordinal));
        Assert.Contains(invalid, f => f.Message.Contains("name", StringComparison.Ordinal));
        Assert.Contains(invalid, f => f.Message.Contains("Unexpected", StringComparison.Ordinal));
    }

    private static List<ValidationResult> MessageFindings(string source, string target) => new MessageSyntaxRule().Validate(new ValidationContext { PrimaryLanguage = "en", Items = [new() { Namespace = "a", Language = "en", Value = source }, new() { Namespace = "a", Language = "fr", Value = target }] }).ToList();

    [Fact]
    public void CjkTermsMatchInsideUnsegmentedTextAndMalformedQuotedCsvIsRejected()
    {
        Assert.True(GlossaryService.ContainsTerm("口座残高", "口座"));
        Assert.Throws<FormatException>(() => GlossaryService.ImportCsv("term,description,forbidden,language,translation\n\"term\"junk,,false,,"));
    }

    [Fact]
    public void ProjectConfigurationCopiesGlossaryAndRulePolicyWithoutSharingMutableState()
    {
        var project = new ProjectSettings { Glossary = [new() { Term = "a", Translations = new() { ["fr"] = "b" } }], ValidationRules = new() { ["glossary"] = new(false, "Error") } };
        var copy = ProjectConfiguration.Copy(project);
        copy.Glossary[0].Translations["fr"] = "c";
        Assert.Equal("b", project.Glossary[0].Translations["fr"]);
        Assert.False(copy.ValidationRules!["glossary"].Enabled);
    }
}
