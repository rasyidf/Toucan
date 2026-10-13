using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;

namespace Toucan.Avalonia.Views.Panels;

/// <summary>Details of the selected key: per-language status, TM matches, similar keys, source usages, history, and what plugins add.</summary>
public partial class InspectorPanel : UserControl
{
    private readonly Dictionary<string, Control> _sectionContent = [];
    private MainWindowViewModel? _vm;

    public InspectorPanel()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            DesktopContributions.Instance.Changed += OnContributionsChanged;
            KeybindingService.Registry.CommandsChanged += OnContributionsChanged;
            Rebuild();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            DesktopContributions.Instance.Changed -= OnContributionsChanged;
            KeybindingService.Registry.CommandsChanged -= OnContributionsChanged;
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as MainWindowViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
            UpdateActionParameters();
        };
    }

    private void OnContributionsChanged(object? sender, EventArgs e) => global::Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedKeyNamespace)) UpdateActionParameters();
    }

    /// <summary>Plugin sections are built once and kept; actions are rebuilt because their availability changes with the project.</summary>
    private void Rebuild()
    {
        var contributions = DesktopContributions.Instance;

        PluginSections.Children.Clear();
        foreach (var section in contributions.InspectorSections)
        {
            if (!_sectionContent.TryGetValue(section.PluginId + "/" + section.Id, out var content))
                _sectionContent[section.PluginId + "/" + section.Id] = content = section.Create();
            PluginSections.Children.Add(new SettingsGroup { Classes = { "compact" }, Header = section.Title.ToUpperInvariant(), Content = content });
        }

        PluginKeyActions.Children.Clear();
        foreach (var action in KeyActions.Collect(KeybindingService.Registry, contributions))
        {
            var button = new Button { Classes = { "inspectorAction" }, Command = action.Command, CommandParameter = _vm?.SelectedKeyNamespace };
            button.Content = action.Icon is { } icon
                ? new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { new FASymbolIcon { Symbol = icon }, new TextBlock { Text = action.Title } } }
                : action.Title;
            global::Avalonia.Automation.AutomationProperties.SetName(button, action.Title);
            PluginKeyActions.Children.Add(button);
        }
        PluginKeyActions.IsVisible = PluginKeyActions.Children.Count > 0;
    }

    private void UpdateActionParameters()
    {
        foreach (var button in PluginKeyActions.Children.OfType<Button>()) button.CommandParameter = _vm?.SelectedKeyNamespace;
    }
}
