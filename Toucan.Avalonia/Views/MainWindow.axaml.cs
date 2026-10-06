using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Views.Panels;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly Dictionary<string, Control> _panelCache = [];
    private bool _closeConfirmed;

    private ColumnDefinition LeftColumn => Workspace.ColumnDefinitions[1];
    private ColumnDefinition RightColumn => Workspace.ColumnDefinitions[5];

    public MainWindow(MainWindowViewModel viewModel, StatusBarViewModel statusBar)
    {
        InitializeComponent();
        _vm = viewModel;

        DataContext = _vm;
        StatusBar.DataContext = statusBar;
        StatusBar.MainViewModel = _vm;

        var registry = SidePanelRegistry.Instance;
        LeftActivityBar.Panels = registry.LeftSlotPanels;
        RightActivityBar.Panels = registry.RightSlotPanels;
        registry.PropertyChanged += OnRegistryChanged;
        PanelService.Instance.PropertyChanged += OnPanelServiceChanged;

        LeftColumn.Width = new GridLength(PanelService.Instance.SidebarWidth);
        RightColumn.Width = new GridLength(PanelService.Instance.InspectorWidth);
        UpdateSlotVisibility();
        ShowLeftPanel(registry.ActiveLeftPanel?.Id);
        ShowRightPanel(registry.ActiveRightPanel?.Id);

        if (PlatformService.IsMacOS) UseUnifiedTitleBar();

        MainMenu.Attach(this, _vm, MenuHost);
        KeybindingService.Apply(this, _vm, MainMenu.NativeGestures);
        AddHandler(KeyDownEvent, (_, e) => KeybindingService.HandleZenKeys(this, e, _vm), RoutingStrategies.Tunnel);

        _vm.FocusSearchRequested += (_, _) => Editor.FocusFilter();

        // Drop a folder (or a file inside one) onto the window to open it.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) await _vm.OpenProjectAsync(path);
        });
        _vm.FullscreenRequested += (_, _) =>
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    /// <summary>
    /// macOS: draw the top bar into the title bar area (like VS Code / Claude) with the system traffic lights
    /// overlaid on the left, instead of a separate native title bar above it.
    /// </summary>
    private void UseUnifiedTitleBar()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;

        const double TrafficLightInset = 78;
        TopBar.MinHeight = 38;
        void UpdateInset() => TopBar.Padding = new Thickness(
            WindowState == WindowState.FullScreen ? 0 : TrafficLightInset, 0, 0, 0); // lights are hidden in fullscreen
        UpdateInset();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateInset(); };
    }

    private void OnRegistryChanged(object? sender, PropertyChangedEventArgs e)
    {
        var registry = SidePanelRegistry.Instance;
        switch (e.PropertyName)
        {
            case nameof(SidePanelRegistry.ActiveLeftPanel):
                ShowLeftPanel(registry.ActiveLeftPanel?.Id);
                break;
            case nameof(SidePanelRegistry.ActiveRightPanel):
                ShowRightPanel(registry.ActiveRightPanel?.Id);
                break;
            case nameof(SidePanelRegistry.LeftSlotVisible):
            case nameof(SidePanelRegistry.RightSlotVisible):
                UpdateSlotVisibility();
                break;
        }
    }

    private void OnPanelServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelService.ZenMode)) UpdateSlotVisibility();
    }

    private void UpdateSlotVisibility()
    {
        var registry = SidePanelRegistry.Instance;
        SetSlot(LeftColumn, LeftPanelHost, LeftSplitter, registry.LeftSlotVisible, PanelService.Instance.SidebarWidth);
        SetSlot(RightColumn, RightPanelHost, RightSplitter, registry.RightSlotVisible, PanelService.Instance.InspectorWidth);
    }

    private static void SetSlot(ColumnDefinition column, Control host, Control splitter, bool visible, double width)
    {
        host.IsVisible = visible;
        splitter.IsVisible = visible;
        column.Width = visible ? new GridLength(Math.Max(160, width)) : new GridLength(0);
    }

    /// <summary>Remembers the current panel widths so they survive hiding and restarts.</summary>
    private void CaptureSlotWidths()
    {
        if (LeftPanelHost.IsVisible && LeftColumn.ActualWidth > 0) PanelService.Instance.SidebarWidth = LeftColumn.ActualWidth;
        if (RightPanelHost.IsVisible && RightColumn.ActualWidth > 0) PanelService.Instance.InspectorWidth = RightColumn.ActualWidth;
    }

    private Control GetPanel(string id)
    {
        if (_panelCache.TryGetValue(id, out var cached)) return cached;
        Control panel = id switch
        {
            "explorer" => new ExplorerPanel(),
            "search" => new SearchPanel(),
            "issues" => new IssuesPanel(),
            "source-code" => new SourceCodePanel(),
            "languages" => new LanguagesPanel(),
            "inspector" => new InspectorPanel(),
            "machine-translation" => new MachineTranslationPanel(),
            "translation-memory" => new TranslationMemoryPanel(),
            _ => new TextBlock { Text = "Unknown panel", Margin = new global::Avalonia.Thickness(12) }
        };
        panel.DataContext = _vm;
        _panelCache[id] = panel;
        return panel;
    }

    private void ShowLeftPanel(string? id)
    {
        CaptureSlotWidths();
        if (id == null) return;
        LeftPanelTitle.Text = SidePanelRegistry.Instance.ActiveLeftPanel?.Title.ToUpperInvariant() ?? string.Empty;
        LeftPanelContent.Content = GetPanel(id);
        FillActions(LeftPanelActions, id switch
        {
            "explorer" =>
            [
                (FASymbol.Add, "Add translation key", _vm.NewItemCommand),
                (FASymbol.List, "Toggle tree / list", _vm.ToggleViewModeCommand),
            ],
            "search" => [(FASymbol.Clear, "Clear search history", _vm.ClearSearchHistoryCommand)],
            "issues" =>
            [
                (FASymbol.Refresh, "Run validation", _vm.RunValidationCommand),
                (FASymbol.Clear, "Dismiss all", _vm.DismissAllIssuesCommand),
            ],
            "source-code" =>
            [
                (FASymbol.Sync, "Scan source code", _vm.ScanSourceCodeCommand),
                (FASymbol.OpenFolder, "Choose source folder", _vm.SelectSourceRootCommand),
            ],
            _ => []
        });
    }

    private void ShowRightPanel(string? id)
    {
        CaptureSlotWidths();
        if (id == null) return;
        RightPanelTitle.Text = SidePanelRegistry.Instance.ActiveRightPanel?.Title.ToUpperInvariant() ?? string.Empty;
        if (id == "machine-translation") _vm.RefreshProviderChoices();
        RightPanelContent.Content = GetPanel(id);
        FillActions(RightPanelActions, id switch
        {
            "languages" =>
            [
                (FASymbol.Add, "Add language", _vm.NewLanguageCommand),
                (FASymbol.Setting, "Manage languages", _vm.ManageLanguagesCommand),
            ],
            "inspector" => [(FASymbol.Character, "Translate selected key", _vm.TranslateSelectedKeyCommand)],
            "machine-translation" =>
            [
                (FASymbol.Character, "Translate selected key", _vm.TranslateSelectedKeyCommand),
                (FASymbol.Setting, "Provider settings", _vm.OpenProviderSettingsCommand),
            ],
            "translation-memory" =>
            [
                (FASymbol.Import, "Import TMX", _vm.ImportTmxCommand),
                (FASymbol.SaveAs, "Export TMX", _vm.ExportTmxCommand),
                (FASymbol.Delete, "Clear translation memory", _vm.ClearTmCommand),
            ],
            _ => []
        });
    }

    private static void FillActions(StackPanel host, (FASymbol Icon, string Tip, ICommand Command)[] actions)
    {
        host.Children.Clear();
        foreach (var (icon, tip, command) in actions)
        {
            var button = new Button { Classes = { "icon", "small" }, Command = command, Content = new FASymbolIcon { Symbol = icon } };
            ToolTip.SetTip(button, Loc.T(tip));
            host.Children.Add(button);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed) return;

        // Ask about unsaved changes first; closing resumes once the prompt resolves.
        e.Cancel = true;
        if (!await _vm.TryCloseProjectAsync()) return;

        CaptureSlotWidths();
        PanelService.Instance.SaveLayout();
        _closeConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        SidePanelRegistry.Instance.PropertyChanged -= OnRegistryChanged;
        PanelService.Instance.PropertyChanged -= OnPanelServiceChanged;
        StatusBarService.Instance.Unregister();
        _vm.Dispose();
        base.OnClosed(e);
    }
}
