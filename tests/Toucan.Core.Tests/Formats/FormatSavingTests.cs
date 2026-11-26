using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>The save-mode labels in Project Properties and formats.md (V1-11) follow how a project was opened.</summary>
public class FormatSavingTests
{
    private static FormatSaveMode For(string format, bool document, bool exact) =>
        FormatSaving.ForProject(FormatTestHost.Factory.GetSaveStrategy(format)!, FormatTestHost.Factory.GetLoadStrategy(format), document, exact);

    [Theory]
    [InlineData("arb", true, false, FormatSaveMode.EditsInPlace)]
    [InlineData("arb", false, false, FormatSaveMode.RewritesFile)]
    [InlineData("arb", false, true, FormatSaveMode.RewritesFile)]
    [InlineData("json", false, true, FormatSaveMode.EditsInPlace)]
    [InlineData("json", false, false, FormatSaveMode.RewritesFile)]
    [InlineData("java-properties", false, true, FormatSaveMode.EditsInPlace)]
    [InlineData("yaml", false, true, FormatSaveMode.RewritesFile)]
    [InlineData("po", false, false, FormatSaveMode.RewritesFile)]
    [InlineData("ini", false, false, FormatSaveMode.ExportOnly)]
    [InlineData("formatjs", false, true, FormatSaveMode.ReadOnly)]
    public void TheModeFollowsTheFormatAndHowTheProjectWasOpened(string format, bool document, bool exact, FormatSaveMode expected) =>
        Assert.Equal(expected, For(format, document, exact));

    [Fact]
    public void EveryModeHasALabelAndAnExplanation()
    {
        foreach (var mode in Enum.GetValues<FormatSaveMode>())
        {
            Assert.NotEmpty(FormatSaving.Label(mode));
            Assert.NotEmpty(FormatSaving.Explain(mode));
        }
    }
}
