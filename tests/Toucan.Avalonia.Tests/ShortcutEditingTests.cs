using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Commands;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class ShortcutEditingTests : IDisposable
{
    private static KeyModifiers Primary => PlatformService.IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control;

    private readonly TestHost _host = new();
    private readonly CommandRegistry _registry = new();

    public ShortcutEditingTests()
    {
        BuiltInCommands.Bind(_registry, null);
    }

    public void Dispose()
    {
        _registry.Dispose();
        _host.Dispose();
    }

    private OptionsViewModel Options() => new(_host.Services.GetRequiredService<IPreferenceService>(), _host.Services.GetRequiredService<IProjectDefaultsService>(),
        _host.Dialogs, _host.Messages, commands: _registry);

    private static ShortcutRowViewModel Row(OptionsViewModel vm, string id) => vm.ShortcutEditGroups.SelectMany(g => g.Rows).Single(r => r.CommandId == id);

    private const string Save = "toucan.file.save";
    private const string Find = "toucan.find.find";

    [Fact]
    public void EveryRegisteredCommandIsListedWithItsShortcut()
    {
        var vm = Options();

        Assert.Equal(_registry.Commands.Count, vm.ShortcutEditGroups.Sum(g => g.Rows.Count));
        Assert.True(Row(vm, Save).HasShortcut);
        Assert.False(Row(vm, Save).IsCustom);
    }

    [Fact]
    public void SearchNarrowsTheListByNameCategoryOrShortcut()
    {
        var vm = Options();
        var all = vm.VisibleShortcutGroups.Sum(g => g.Rows.Count);

        vm.ShortcutFilter = "save";
        Assert.Contains(vm.VisibleShortcutGroups.SelectMany(g => g.Rows), r => r.CommandId == Save);
        Assert.All(vm.VisibleShortcutGroups.SelectMany(g => g.Rows), r => Assert.Contains("save", r.Title + r.Category + r.Shortcut + r.CommandId, StringComparison.OrdinalIgnoreCase));

        vm.ShortcutFilter = "file save"; // words from the category and the name
        Assert.Equal([Save], vm.VisibleShortcutGroups.SelectMany(g => g.Rows).Select(r => r.CommandId).Where(id => id == Save));

        vm.ShortcutFilter = "zzzz";
        Assert.True(vm.HasNoShortcutMatches);

        vm.ShortcutFilter = string.Empty;
        Assert.Equal(all, vm.VisibleShortcutGroups.Sum(g => g.Rows.Count));
    }

    [Fact]
    public void ChangingTheFilterCancelsAnEditInProgress()
    {
        var vm = Options();
        var row = Row(vm, Save);
        vm.Change(row);

        vm.ShortcutFilter = "find";

        Assert.False(row.IsCapturing);
        Assert.False(vm.IsCapturingShortcut);
    }

    [Theory]
    [InlineData(Key.K, false, true)]   // plain letter: would break typing
    [InlineData(Key.F9, false, false)] // function keys stand alone
    [InlineData(Key.Delete, false, false)]
    public void PlainKeysAreRefusedUnlessTheyStandAlone(Key key, bool withPrimary, bool refused)
    {
        var text = KeybindingService.ToPortable(key, withPrimary ? Primary : KeyModifiers.None, out var problem);

        Assert.Equal(refused, text is null);
        Assert.Equal(refused, problem is not null);
    }

    [Fact]
    public void ModifierOnlyPressesAreIgnoredWithoutAMessage()
    {
        Assert.Null(KeybindingService.ToPortable(Key.LeftShift, KeyModifiers.Shift, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void ThePrimaryModifierIsWrittenAsMod()
    {
        Assert.Equal("Mod+Shift+K", KeybindingService.ToPortable(Key.K, Primary | KeyModifiers.Shift, out _));
    }

    [Fact]
    public void ChangingAShortcutAppliesAtOnce()
    {
        var vm = Options();
        var row = Row(vm, Save);

        vm.Change(row);
        Assert.True(row.IsCapturing);
        Assert.True(vm.IsCapturingShortcut);
        Assert.True(vm.HandleShortcutKey(Key.J, Primary | KeyModifiers.Alt));

        Assert.False(row.IsCapturing);
        Assert.True(row.IsCustom);
        Assert.Equal("Mod+Alt+J", _registry.GetShortcut(Save));
    }

    [Fact]
    public void KeysPassThroughWhenNothingIsBeingEdited()
    {
        Assert.False(Options().HandleShortcutKey(Key.J, Primary));
    }

    [Fact]
    public void EscapeCancelsTheEdit()
    {
        var vm = Options();
        var row = Row(vm, Save);
        var before = _registry.GetShortcut(Save);

        vm.Change(row);
        vm.HandleShortcutKey(Key.Escape, KeyModifiers.None);

        Assert.False(row.IsCapturing);
        Assert.Equal(before, _registry.GetShortcut(Save));
    }

    [Fact]
    public void AConflictNamesTheOtherCommandAndNeedsASecondPress()
    {
        var vm = Options();
        var row = Row(vm, Save);
        var findShortcut = _registry.GetShortcut(Find)!;
        Assert.True(ShortcutText.TryNormalize(findShortcut, out _));
        // Mod+F is Find; press it for Save.
        vm.Change(row);
        vm.HandleShortcutKey(Key.F, Primary);

        Assert.True(row.IsCapturing);
        Assert.Contains("Find", row.Message);
        Assert.NotEqual(findShortcut, _registry.GetShortcut(Save));

        vm.HandleShortcutKey(Key.F, Primary);

        Assert.False(row.IsCapturing);
        Assert.Equal(findShortcut, _registry.GetShortcut(Save));
        Assert.Null(_registry.GetShortcut(Find));
        Assert.False(Row(vm, Find).HasShortcut);
        Assert.True(Row(vm, Find).IsCustom); // explicitly unbound
    }

    [Fact]
    public void ResetAndClearAndResetAll()
    {
        var vm = Options();
        var row = Row(vm, Save);
        var original = _registry.GetShortcut(Save);

        vm.Clear(row);
        Assert.False(row.HasShortcut);

        vm.Reset(row);
        Assert.Equal(original, _registry.GetShortcut(Save));
        Assert.False(row.IsCustom);

        vm.Change(row);
        vm.HandleShortcutKey(Key.J, Primary | KeyModifiers.Alt);
        vm.ResetAllShortcutsCommand.Execute(null);
        Assert.Equal(original, _registry.GetShortcut(Save));
        Assert.Empty(_registry.CustomShortcuts);
    }

    [Fact]
    public void CancellingTheDialogRestoresTheShortcuts()
    {
        var vm = Options();
        var original = _registry.GetShortcut(Save);
        vm.Change(Row(vm, Save));
        vm.HandleShortcutKey(Key.J, Primary | KeyModifiers.Alt);

        vm.RevertShortcutEdits();

        Assert.Equal(original, _registry.GetShortcut(Save));
    }

    [Fact]
    public void SavingKeepsTheShortcutsAndStoresThemInTheOptions()
    {
        var vm = Options();
        vm.Change(Row(vm, Save));
        vm.HandleShortcutKey(Key.J, Primary | KeyModifiers.Alt);

        vm.SaveCommand.Execute(null);
        vm.RevertShortcutEdits(); // runs when the dialog closes; must not undo a saved edit

        Assert.Equal("Mod+Alt+J", _registry.GetShortcut(Save));
        Assert.Equal("Mod+Alt+J", vm.AppOptions.CustomShortcuts[Save]);
    }

    [AvaloniaFact]
    public void SavedShortcutsComeBackWhenTheAppBindsItsCommands()
    {
        var previous = KeybindingService.Registry;
        KeybindingService.UseRegistry(_registry);
        try
        {
            var vm = _host.CreateViewModel();
            vm.AppOptions.CustomShortcuts[Save] = "Mod+Alt+J";
            var window = new global::Avalonia.Controls.Window();

            KeybindingService.Apply(window, vm);

            Assert.Equal("Mod+Alt+J", _registry.GetShortcut(Save));
            Assert.Contains(window.KeyBindings, b => b.Gesture == KeybindingService.ToGesture("Mod+Alt+J"));
        }
        finally
        {
            KeybindingService.UseRegistry(previous);
        }
    }
}
