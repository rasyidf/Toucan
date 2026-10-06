using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Behaviours the WPF app had that the Avalonia app gained when WPF was retired.</summary>
public class WpfParityTests
{
    [AvaloniaFact]
    public async Task TrimLineByLine_TrimsEveryLineOfMultilineValues()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("trim", ("en", """{"msg": "  first  \n   second   \n third"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        await vm.TrimLineByLineCommand.ExecuteAsync(null);

        Assert.Equal("first\nsecond\nthird", vm.AllTranslation.Single(t => t.Namespace == "msg").Value);
    }

    [AvaloniaFact]
    public async Task CutKeyValues_RemovesTheKeyAfterConfirming()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("cut", ("en", """{"a": "A", "b": "B"}"""), ("fr", """{"a": "AA", "b": "BB"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "a");
        host.Messages.ConfirmAnswer = true;

        await vm.EditCutCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.AllTranslation, t => t.Namespace == "a");
        Assert.Contains(vm.AllTranslation, t => t.Namespace == "b");
    }

    [AvaloniaFact]
    public async Task CutKeyValues_KeepsTheKeyWhenTheUserDeclines()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("cutno", ("en", """{"a": "A"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.SelectedGroup = vm.PagingController.Data.Single();
        host.Messages.ConfirmAnswer = false;

        await vm.EditCutCommand.ExecuteAsync(null);

        Assert.Contains(vm.AllTranslation, t => t.Namespace == "a");
    }

    [AvaloniaFact]
    public void EveryCopyTemplateHasAShortcutThatPassesItsIndex()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var window = new Window();

        KeybindingService.Apply(window, vm);

        var indexes = window.KeyBindings.Where(b => ReferenceEquals(b.Command, vm.CopyAsTemplateCommand)).Select(b => b.CommandParameter).Cast<int>().Order().ToList();
        Assert.Equal([0, 1, 2, 3, 4], indexes);
        Assert.Equal(5, KeybindingService.GetDefinitions().Count(d => d.Action.StartsWith("Copy Template", StringComparison.Ordinal)));
    }
}
