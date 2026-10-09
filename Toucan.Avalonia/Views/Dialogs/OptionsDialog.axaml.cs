using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class OptionsDialog : DialogWindow
{
    public OptionsDialog() => InitializeComponent();

    public OptionsDialog(OptionsViewModel vm) : this()
    {
        DataContext = vm;
        FillPluginSettings();
        vm.CloseAction = ok => Close(ok);
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.RevertSchemePreview();
            vm.RevertShortcutEdits();
        };
        // Tunnel, so the pressed combination reaches the shortcut being edited instead of a focused button or a menu.
        AddHandler(KeyDownEvent, (_, e) => e.Handled = vm.HandleShortcutKey(e.Key, e.KeyModifiers), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Adds the settings sections plugins registered, each under its own heading on the Plugins page.</summary>
    private void FillPluginSettings()
    {
        if (this.FindControl<StackPanel>("PluginSettingsHost") is not { } host) return;
        // The typed settings plugins declared: a generated form for each.
        var workspace = Services.DesktopContributions.Instance.Workspace;
        foreach (var entry in Services.DesktopContributions.Instance.Configurations)
            host.Children.Add(new SettingsGroup { Header = Loc.Format("{0} settings", entry.Name), Content = new ConfigForm(entry.Configuration, () => workspace?.WorkspaceId) });

        foreach (var section in Services.DesktopContributions.Instance.SettingsSections)
        {
            var body = new StackPanel { Spacing = 8 };
            if (!string.IsNullOrWhiteSpace(section.Description))
                body.Children.Add(new TextBlock { Text = section.Description, Classes = { "caption" }, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new global::Avalonia.Thickness(16, 12, 16, 0) });
            body.Children.Add(section.Create());
            host.Children.Add(new SettingsGroup { Header = section.Title, Content = body });
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OptionsViewModel vm) return;
        if (e.PropertyName == nameof(OptionsViewModel.SearchText)) ApplySearch(vm);
        // Picking a page in the sidebar leaves search mode and shows that page.
        else if (e.PropertyName == nameof(OptionsViewModel.SelectedPageIndex) && vm.IsSearching) vm.SearchText = string.Empty;
    }

    /// <summary>
    /// Shows only the settings whose title, description, group header or page name contains every typed word.
    /// Rows and groups collapse through <c>IsMatch</c> so the page bindings on <c>IsVisible</c> stay intact.
    /// </summary>
    private void ApplySearch(OptionsViewModel vm)
    {
        var terms = vm.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pagesPanel = this.FindControl<StackPanel>("PagesPanel");
        if (pagesPanel is null) return;
        var pages = pagesPanel.Children.OfType<StackPanel>().ToList();
        var matches = new bool[pages.Count];

        for (var i = 0; i < pages.Count; i++)
        {
            var pageName = i < OptionsViewModel.NavEntries.Count ? Loc.T(OptionsViewModel.NavEntries[i].Title) : string.Empty;
            var pageMatches = terms.Length == 0 || Matches(pageName, terms);
            var any = false;

            foreach (var group in pages[i].GetVisualDescendants().OfType<ISettingsSection>().Cast<Control>())
            {
                var section = (ISettingsSection)group;
                var rows = group.GetVisualDescendants().OfType<SettingsRow>().ToList();
                var groupMatches = pageMatches || Matches(section.Header, terms);
                var rowHits = rows.Where(r => Matches($"{r.Title} {r.Description}", terms)).ToList();

                foreach (var row in rows)
                {
                    var untitled = string.IsNullOrEmpty(row.Title);
                    row.IsMatch = groupMatches || rowHits.Contains(row) || untitled && rowHits.Count > 0;
                }
                section.IsMatch = groupMatches || rowHits.Count > 0;
                any |= section.IsMatch;
            }
            matches[i] = terms.Length == 0 || any || pageMatches;
        }

        vm.SearchMatches = matches;
    }

    private static bool Matches(string? text, string[] terms) =>
        terms.Length == 0 || !string.IsNullOrEmpty(text) && terms.All(t => text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
}
