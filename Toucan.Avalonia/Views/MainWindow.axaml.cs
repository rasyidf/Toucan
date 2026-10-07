using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    private ColumnDefinition LeftColumn => Workspace.ColumnDefinitions[0];
    private ColumnDefinition RightColumn => Workspace.ColumnDefinitions[4];

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

        UseUnifiedTitleBar();
        TopBar.LayoutUpdated += (_, _) => UpdateTitleBarLayout();
        Workspace.SizeChanged += (_, _) => FitSidePanels();
        // Clip the content as well as the outline: collapsing either panel brings
        // the editor background directly against these rounded corners.
        WorkspaceContent.SizeChanged += (_, e) => WorkspaceContent.Clip =
            new RectangleGeometry(new Rect(e.NewSize), 8, 8);

        MainMenu.Attach(this, _vm, MenuHost);
        Palette.CommandSource = () => MainMenu.PaletteCommands(this, _vm);
        PaletteShortcut.Text = KeybindingService.GestureFor("Command Palette")?.ToString("p", null);
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
    /// Draw the top bar into the title bar area (like VS Code / Claude) with the window controls overlaid, instead of a separate
    /// native title bar above it: the traffic lights on the left on macOS, the minimize, maximize and close buttons on the right
    /// on Windows and Linux.
    /// </summary>
    private void UseUnifiedTitleBar()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;

        TopBar.MinHeight = 40;
        if (!PlatformService.IsMacOS) UseOwnWindowButtons();
        // No native title shows the app name any more, so the top bar carries it on every platform.
        TitleBarBrand.IsVisible = true;
        if (!PlatformService.IsMacOS)
        {
            // macOS drags the title bar natively. Elsewhere the empty part of the bar is the drag area (it needs a background to be
            // hit-testable) and the controls on it are marked so they keep receiving clicks.
            TopBar.Background = Brushes.Transparent;
            WindowDecorationProperties.SetElementRole(TopBar, WindowDecorationsElementRole.TitleBar);
            foreach (Control control in new Control[] { MenuHost, SaveStateButton, PalettePill, TitleBarTrailing })
                WindowDecorationProperties.SetElementRole(control, WindowDecorationsElementRole.User);
        }

        void UpdateInsets()
        {
            var (leading, trailing) = PlatformService.TitleBarInsets(PlatformService.IsMacOS, WindowState == WindowState.FullScreen);
            TitleBarLeading.Margin = new Thickness(leading, 0, 0, 0);
            TitleBarTrailing.Margin = new Thickness(0, 0, trailing, 0);
            ZenView.SetTitleBarInset(leading, trailing); // Zen mode covers the top bar, so it needs the same clearance
            UpdateTitleBarLayout();
        }
        UpdateInsets();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateInsets();
            // Maximized Windows windows extend past the screen edge by the frame; keep the content on screen.
            else if (e.Property == OffScreenMarginProperty) RootGrid.Margin = OffScreenMargin;
        };
    }

    /// <summary>
    /// Windows and Linux: no system title bar (the window keeps only its border) so nothing draws a title or icon over the menu;
    /// the top bar carries minimize, maximize/restore and close buttons instead.
    /// </summary>
    private void UseOwnWindowButtons()
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.BorderOnly;
        CaptionButtons.IsVisible = true;
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();

        void Sync()
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeGlyph.IsVisible = !maximized;
            RestoreGlyph.IsVisible = maximized;
            ToolTip.SetTip(MaximizeButton, Loc.T(maximized ? "Restore" : "Maximize"));
            CaptionButtons.IsVisible = WindowState != WindowState.FullScreen;
        }
        Sync();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) Sync(); };
    }

    private void UpdateTitleBarLayout()
    {
        // Equal reserved wings keep search centered even when the native controls are visible. A wide menu (Windows and Linux)
        // can leave too little room that way, so then settle for whatever is free between the two sides.
        var leading = TitleBarLeading.DesiredSize.Width;
        var trailing = TitleBarTrailing.DesiredSize.Width;

        // The menu folds into "…" rather than overlap the search pill and the controls on the right.
        var beside = leading - MenuHost.DesiredSize.Width;
        var menuRoom = Math.Max(0, TitleBarLayout.Bounds.Width - beside - trailing - 180 - 40);
        if (double.IsInfinity(MenuHost.MaxWidth) || Math.Abs(MenuHost.MaxWidth - menuRoom) > 0.5) MenuHost.MaxWidth = menuRoom;
        var wing = Math.Max(leading, trailing) + 16;
        var free = TitleBarLayout.Bounds.Width - leading - trailing - 32;
        var width = Math.Clamp(TitleBarLayout.Bounds.Width - 2 * wing, Math.Clamp(free, 180, 280), 500);
        if (Math.Abs(PalettePill.Width - width) > 0.5) PalettePill.Width = width;
    }

    private void FitSidePanels()
    {
        if (Workspace.Bounds.Width <= 0) return;
        var left = LeftPanelHost.IsVisible ? LeftColumn.Width.Value : 0;
        var right = RightPanelHost.IsVisible ? RightColumn.Width.Value : 0;
        var available = Math.Max(320, Workspace.Bounds.Width - 360 - 8);
        if (left + right <= available) return;
        if (left > 0 && right > 0)
        {
            var fittedLeft = Math.Clamp(available * left / (left + right), 160, available - 160);
            LeftColumn.Width = new GridLength(fittedLeft);
            RightColumn.Width = new GridLength(available - fittedLeft);
        }
        else if (left > 0) LeftColumn.Width = new GridLength(available);
        else if (right > 0) RightColumn.Width = new GridLength(available);
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
        FitSidePanels();
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
