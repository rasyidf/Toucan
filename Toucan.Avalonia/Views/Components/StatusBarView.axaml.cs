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
        NotificationsButton.Click += OnNotificationsClick;
        OperationsButton.Click += OnOperationsClick;
        EncodingButton.Click += OnEncodingClick;
        LineEndingButton.Click += OnLineEndingClick;
        LanguageButton.Click += OnLanguageClick;
    }

    public MainWindowViewModel? MainViewModel { get; set; }

    /// <summary>Lists what is running, each with what it says and a Cancel item when it can be stopped.</summary>
    private void OnOperationsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusBarViewModel status) return;
        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        foreach (var operation in status.Operations.Items.ToList())
        {
            flyout.Items.Add(new MenuItem { Header = OperationsViewModel.Describe(operation, withPercent: true), IsEnabled = false });
            if (operation.IsCancellable)
                flyout.Items.Add(new MenuItem { Header = Locales.Loc.T("Cancel"), Command = new CommunityToolkit.Mvvm.Input.RelayCommand(operation.Cancel) });
        }
        if (flyout.Items.Count > 0) flyout.ShowAt(OperationsButton);
    }

    private void OnEncodingClick(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is not { } vm || !vm.CanChangeFileFormat || DataContext is not StatusBarViewModel status) return;
        if (!vm.CanChangeTextEncoding)
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
            flyout.Items.Add(new MenuItem { Header = "This format uses ISO-8859-1 encoding", IsEnabled = false });
            flyout.ShowAt(EncodingButton);
            return;
        }
        ShowChoice(EncodingButton, "Save project files with encoding", new[] { "UTF-8", "UTF-8 BOM" }, status.Encoding.Encoding, vm.SetTextEncoding);
    }

    private void OnLineEndingClick(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is not { } vm || !vm.CanChangeFileFormat || DataContext is not StatusBarViewModel status) return;
        ShowChoice(LineEndingButton, "Save project files with line endings", new[] { "LF", "CRLF" }, status.LineEndings.LineEnding, vm.SetLineEnding);
    }

    private static void ShowChoice(Control target, string title, IEnumerable<string> choices, string current, Action<string> select)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        foreach (var choice in choices)
        {
            var item = new MenuItem { Header = choice, ToggleType = MenuItemToggleType.Radio, IsChecked = choice == current };
            item.Click += (_, _) => select(choice);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(target);
    }

    private void OnNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is not { } vm || DataContext is not StatusBarViewModel status) return;
        var notifications = status.Notifications;
        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = $"Untranslated entries ({notifications.Untranslated})", Command = vm.ShowUntranslatedCommand });
        var issues = new MenuItem { Header = $"Validation: {notifications.Errors} errors, {notifications.Warnings} warnings, {notifications.Info} info" };
        issues.Click += (_, _) => Toucan.Core.Services.SidePanelRegistry.Instance.Activate("issues");
        flyout.Items.Add(issues);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Run validation", Command = vm.RunValidationCommand });
        flyout.ShowAt(NotificationsButton);
    }

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
