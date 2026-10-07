using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Inline TM suggestion: set on focus, cleared on blur, accepted into an empty field only.</summary>
public class GhostTextTests
{
    private static async Task<(TestHost Host, MainWindowViewModel Vm, TranslationItemViewModel Fr, TranslationItemViewModel Fr2)> Open()
    {
        var host = new TestHost();
        host.Services.GetRequiredService<ITranslationMemory>().Add("Cancel", "Annuler", "en", "fr");
        var folder = host.CreateJsonProject("ghost", ("en", """{"a": "Cancel", "b": "Cancel", "c": "Other"}"""), ("fr", """{"b": "Déjà", "c": "Autre"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        var fr = vm.PagingController.Data.Single(g => g.Namespace == "a").Translations.Single(t => t.Language == "fr");
        var fr2 = vm.PagingController.Data.Single(g => g.Namespace == "b").Translations.Single(t => t.Language == "fr");
        return (host, vm, fr, fr2);
    }

    [AvaloniaFact]
    public async Task FocusingAnEmptyField_ShowsTheMemoryMatch_AndMovingFocusClearsIt()
    {
        var (host, vm, fr, fr2) = await Open();
        using var _ = host;

        vm.FocusedTranslationItem = fr;
        Assert.Equal("Annuler", fr.GhostText);
        Assert.True(fr.HasGhostText);
        Assert.Equal(string.Empty, fr.PlaceholderText);

        vm.FocusedTranslationItem = fr2;
        Assert.Null(fr.GhostText);
        Assert.NotEqual(string.Empty, fr.PlaceholderText);
    }

    [AvaloniaFact]
    public async Task Accept_FillsAnEmptyField_ButNeverOverwritesAValue()
    {
        var (host, vm, fr, fr2) = await Open();
        using var _ = host;

        vm.FocusedTranslationItem = fr;
        Assert.True(vm.AcceptGhostText(fr));
        Assert.Equal("Annuler", fr.Value);
        Assert.Null(fr.GhostText);
        Assert.False(vm.AcceptGhostText(fr));

        vm.FocusedTranslationItem = fr2;
        fr2.GhostText = "Annuler";
        Assert.False(vm.AcceptGhostText(fr2));
        Assert.Equal("Déjà", fr2.Value);
    }

    [AvaloniaFact]
    public async Task Accept_IsIgnoredInAuditMode_AndWhenAutoSuggestIsOff()
    {
        var (host, vm, fr, _) = await Open();
        using var _h = host;

        vm.AppOptions.TmAutoSuggest = false;
        vm.FocusedTranslationItem = fr;
        Assert.Null(fr.GhostText);

        vm.AppOptions.TmAutoSuggest = true;
        vm.SwitchToAuditModeCommand.Execute(null);
        fr.GhostText = "Annuler";
        Assert.False(vm.AcceptGhostText(fr));
        Assert.True(fr.IsEmpty);
    }
}

/// <summary>Shortcut sheet: the command toggles it, and no two shortcuts share a gesture.</summary>
public class ShortcutSheetTests
{
    [AvaloniaFact]
    public void ToggleCommand_OpensAndClosesTheSheet()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        vm.ToggleShortcutSheetCommand.Execute(null);
        Assert.True(vm.IsShortcutSheetOpen);
        vm.ToggleShortcutSheetCommand.Execute(null);
        Assert.False(vm.IsShortcutSheetOpen);
    }

    [AvaloniaFact]
    public void Definitions_ListTheSheetShortcut_WithoutDuplicateGestures()
    {
        var defs = Toucan.Avalonia.Services.KeybindingService.GetDefinitions().Where(d => !d.Shortcut.Contains(" or ")).ToList();
        Assert.Contains(defs, d => d.Action == "Keyboard Shortcuts" && d.Shortcut.EndsWith('/'));
        Assert.Empty(defs.GroupBy(d => d.Shortcut).Where(g => g.Count() > 1).Select(g => g.Key));
    }
}
