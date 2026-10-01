using Avalonia.Controls;
using Avalonia.Interactivity;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// Bottom status bar. Panels are data-bound to <see cref="StatusBarViewModel"/>; clicks are
/// forwarded to the main window view model (project properties, mode cycling, validation).
/// </summary>
public partial class StatusBarView : UserControl
{
    public StatusBarView()
    {
        InitializeComponent();
        ProjectButton.Click += (_, _) => MainViewModel?.ShowProjectPropertiesCommand.Execute(null);
        ModeButton.Click += (_, _) => MainViewModel?.CycleEditorModeCommand.Execute(null);
        StatsButton.Click += (_, _) => MainViewModel?.RunValidationCommand.Execute(null);
        NotificationsButton.Click += (_, _) => MainViewModel?.ShowUntranslatedCommand.Execute(null);
        LanguageButton.Click += OnLanguageClick;
    }

    public MainWindowViewModel? MainViewModel { get; set; }

    private void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusBarViewModel sb || MainViewModel is not { HasProject: true } vm) return;

        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = "Primary (source) language", IsEnabled = false });
        foreach (var lang in sb.AvailableLanguages)
        {
            var item = new MenuItem
            {
                Header = lang,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(lang, sb.DefaultLanguage, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => vm.SetPrimaryLanguage(lang);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Manage Languages…", Command = vm.ManageLanguagesCommand });
        flyout.ShowAt(LanguageButton);
    }
}
