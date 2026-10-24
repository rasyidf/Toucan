using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Core.Commands;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class ProjectMenuVisibilityTests
{
    private static List<List<string>> ReadGroups(Window window, ContentControl host)
    {
        var groups = new List<List<string>>();
        void ReadItems(IEnumerable<object> items)
        {
            var group = new List<string>(); groups.Add(group);
            foreach (var item in items)
            {
                switch (item)
                {
                    case Separator or NativeMenuItemSeparator: group.Add("-"); break;
                    case MenuItem menu:
                        group.Add(menu.Header?.ToString()?.Replace("_", "", StringComparison.Ordinal) ?? "");
                        if (menu.Items.Count > 0) ReadItems(menu.Items.Cast<object>());
                        break;
                    case NativeMenuItem native:
                        group.Add(native.Header ?? "");
                        if (native.Menu != null) ReadItems(native.Menu.Items.Cast<object>());
                        break;
                }
            }
        }
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        if (PlatformService.IsMacOS)
        {
            ReadItems(NativeMenu.GetMenu(window)!.Items.Cast<object>());
            Assert.All(MainMenu.NativeGestures, gesture => Assert.DoesNotContain(window.KeyBindings, binding => gesture.Equals(binding.Gesture)));
        }
        else ReadItems(Assert.IsType<ResponsiveMenu>(host.Content).Items.Cast<object>());
        foreach (var group in groups)
        {
            Assert.NotEmpty(group); Assert.NotEqual("-", group[0]); Assert.NotEqual("-", group[^1]);
            Assert.DoesNotContain(group.Zip(group.Skip(1)), pair => pair.First == "-" && pair.Second == "-");
        }
        return groups;
    }

    [AvaloniaTheory]
    [InlineData(300)]
    [InlineData(1200)]
    public async Task ProjectActionsFollowOpenAndCloseWithoutOrphanSeparators(int width)
    {
        using var test = new TestHost(); var vm = test.CreateViewModel();
        var host = new ContentControl(); var window = new Window { Content = host, Width = width, Height = 200 };
        MainMenu.Attach(window, vm, host); window.Show();
        try
        {
            var projectActions = new[] { "Save", "Save As…", "Close Project", "Import Translations…", "Export…", "Project Properties…", "Pre-translate…", "Run Validation", "Statistics…", "Migration report…", "Learn from This Project", "Toggle Tree / List", "Zen Mode" };
            void AssertClosed()
            {
                var headers = ReadGroups(window, host).SelectMany(g => g).ToList();
                Assert.All(projectActions, action => Assert.DoesNotContain(action, headers));
                Assert.Contains("New Project…", headers); Assert.Contains("Open Folder…", headers);
                Assert.Contains("Translation Providers…", headers); Assert.Contains("Import TMX…", headers);
                Assert.Contains("Plugins…", headers); Assert.Contains("Documentation", headers);
                var titles = MainMenu.PaletteCommands(window, vm).Select(c => c.Title).ToList();
                Assert.DoesNotContain("Save", titles); Assert.DoesNotContain("Pre-translate…", titles);
                Assert.Contains("New Project…", titles);
            }
            AssertClosed();
            await vm.OpenProjectAsync(test.CreateJsonProject("menus", ("en", "{\"a\":\"Hello\"}")));
            var opened = ReadGroups(window, host).SelectMany(g => g).ToList();
            Assert.All(projectActions, action => Assert.Contains(action, opened));
            Assert.Contains(MainMenu.PaletteCommands(window, vm), c => c.Title == "Pre-translate…");
            await vm.CloseProjectCommand.ExecuteAsync(null);
            Assert.False(vm.HasProject); AssertClosed();
        }
        finally { window.Close(); }
    }

    private sealed class Handler : ICommandHandler
    {
        public CommandState GetState(ICommandContext context) => CommandState.Available;
        public Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task WorkspacePluginActionsAreHiddenButGlobalActionsStayVisible()
    {
        using var test = new TestHost(); var vm = test.CreateViewModel();
        var previous = KeybindingService.Registry; var registry = test.Services.GetRequiredService<ICommandRegistry>();
        KeybindingService.UseRegistry(registry);
        var host = new ContentControl(); var window = new Window { Content = host, Width = 1200, Height = 200 };
        try
        {
            registry.Register(new CommandDefinition { Id = "test.menu.workspace", Title = "Project plugin action", Category = "Test", RequiresWorkspace = true }, new Handler(), "test.menu");
            registry.Register(new CommandDefinition { Id = "test.menu.global", Title = "Global plugin action", Category = "Test", RequiresWorkspace = false }, new Handler(), "test.menu");
            MainMenu.Attach(window, vm, host); window.Show();
            void Check(bool opened)
            {
                var headers = ReadGroups(window, host).SelectMany(g => g).ToList();
                Assert.Equal(opened, headers.Contains("Project plugin action")); Assert.Contains("Global plugin action", headers);
                var titles = MainMenu.PaletteCommands(window, vm).Select(c => c.Title).ToList();
                Assert.Equal(opened, titles.Contains("Project plugin action")); Assert.Contains("Global plugin action", titles);
            }
            Check(false);
            await vm.OpenProjectAsync(test.CreateJsonProject("plugin-menu", ("en", "{\"a\":\"Hello\"}"))); Check(true);
            await vm.CloseProjectCommand.ExecuteAsync(null); Check(false);
        }
        finally { window.Close(); KeybindingService.UseRegistry(previous); }
    }
}
