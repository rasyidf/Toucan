using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Color scheme editor (Appearance page) and update check (About page).</summary>
public partial class OptionsViewModel
{
    public static IReadOnlyList<string> SchemeNames => ColorSchemeService.SchemeNames;
    public static IReadOnlyList<ColorSchemePreset> SchemePresets => ColorSchemeService.Presets;
    public static IReadOnlyList<string> VariantOptions { get; } = ["Light", "Dark"];
    public static IReadOnlyList<string> UpdateChannelOptions { get; } = ["Stable", "Preview"];

    [ObservableProperty] private string colorScheme = "Toucan";
    [ObservableProperty] private string accentHex = ColorSchemeService.Presets[0].Accent;
    [ObservableProperty] private string editingVariant = "Light";
    [ObservableProperty] private IBrush accentBrush = Brush.Parse(ColorSchemeService.Presets[0].Accent);
    [ObservableProperty] private bool accentInvalid;

    /// <summary>Colors of the variant being edited (<see cref="EditingVariant"/>).</summary>
    public ObservableCollection<SchemeColorItem> SchemeColorItems { get; } = [];

    /// <summary>"Light:CardBackgroundBrush" → "#RRGGBB" for every color that differs from the default.</summary>
    private Dictionary<string, string> _schemeOverrides = [];
    private bool _schemeLoading;

    /// <summary>Scheme as saved on disk; what Cancel puts back after a live preview.</summary>
    private (string Scheme, string? Accent, Dictionary<string, string> Colors) _savedScheme;

    private void LoadSchemeFromOptions()
    {
        _schemeLoading = true;
        var opts = AppOptions;
        _schemeOverrides = new Dictionary<string, string>(opts.SchemeColors ?? [], StringComparer.OrdinalIgnoreCase);
        var preset = ColorSchemeService.PresetNamed(opts.ColorScheme);
        ColorScheme = preset?.Name ?? (string.IsNullOrEmpty(opts.AccentColor) ? ColorSchemeService.Presets[0].Name : ColorSchemeService.Custom);
        AccentHex = ColorSchemeService.TryParseColor(opts.AccentColor, out var accent) ? ColorSchemeService.Hex(accent) : (preset ?? ColorSchemeService.Presets[0]).Accent;
        EditingVariant = global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess() && global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? "Dark" : "Light";
        var firstLoad = _savedScheme.Colors is null;
        if (firstLoad) _savedScheme = (opts.ColorScheme, opts.AccentColor, new Dictionary<string, string>(_schemeOverrides));
        _schemeLoading = false;
        RebuildSchemeItems();
        if (!firstLoad) PreviewScheme(); // "Reset all settings" shows the defaults live; Cancel still restores the saved scheme
        UpdateChannel = UpdateChannelOptions.Contains(opts.UpdateChannel) ? opts.UpdateChannel : "Stable";
    }

    private void SaveSchemeToOptions()
    {
        AppOptions.ColorScheme = ColorScheme;
        AppOptions.AccentColor = AccentHex;
        AppOptions.SchemeColors = new Dictionary<string, string>(_schemeOverrides);
        AppOptions.UpdateChannel = UpdateChannel;
        _savedScheme = (AppOptions.ColorScheme, AppOptions.AccentColor, new Dictionary<string, string>(_schemeOverrides));
    }

    /// <summary>Puts the saved scheme back; called when the dialog closes without saving.</summary>
    public void RevertSchemePreview() => ColorSchemeService.Apply(_savedScheme.Scheme, _savedScheme.Accent, _savedScheme.Colors);

    private void PreviewScheme()
    {
        if (_schemeLoading) return;
        ColorSchemeService.Apply(ColorScheme, AccentHex, _schemeOverrides);
    }

    partial void OnColorSchemeChanged(string value)
    {
        if (_schemeLoading) return;
        if (ColorSchemeService.PresetNamed(value) is { } preset && !string.Equals(AccentHex, preset.Accent, StringComparison.OrdinalIgnoreCase))
        {
            _schemeLoading = true;
            AccentHex = preset.Accent;
            _schemeLoading = false;
            UpdateAccentBrush();
        }
        PreviewScheme();
    }

    partial void OnAccentHexChanged(string value)
    {
        if (_schemeLoading) return;
        AccentInvalid = !ColorSchemeService.TryParseColor(value, out _);
        if (AccentInvalid) return;
        UpdateAccentBrush();
        // Typing a color that is not a preset makes the scheme "Custom".
        var match = ColorSchemeService.Presets.FirstOrDefault(p => string.Equals(p.Accent, value.Trim(), StringComparison.OrdinalIgnoreCase));
        _schemeLoading = true;
        ColorScheme = match?.Name ?? ColorSchemeService.Custom;
        _schemeLoading = false;
        PreviewScheme();
    }

    private void UpdateAccentBrush()
    {
        if (ColorSchemeService.TryParseColor(AccentHex, out var c)) AccentBrush = new SolidColorBrush(c);
    }

    partial void OnEditingVariantChanged(string value) { if (!_schemeLoading) RebuildSchemeItems(); }

    private ThemeVariant Variant => EditingVariant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private void RebuildSchemeItems()
    {
        SchemeColorItems.Clear();
        foreach (var token in ColorSchemeService.Tokens)
        {
            var key = $"{EditingVariant}:{token.Key}";
            var item = new SchemeColorItem(token.Label, ColorSchemeService.Hex(ColorSchemeService.DefaultColor(Variant, token.Key)), OnSchemeColorEdited, key);
            item.Load(_schemeOverrides.TryGetValue(key, out var hex) ? hex : null);
            SchemeColorItems.Add(item);
        }
    }

    private void OnSchemeColorEdited(SchemeColorItem item)
    {
        if (item.IsOverridden && item.IsValid) _schemeOverrides[item.StorageKey] = ColorSchemeService.TryParseColor(item.Hex, out var c) ? ColorSchemeService.Hex(c) : item.Hex;
        else if (!item.IsOverridden) _schemeOverrides.Remove(item.StorageKey);
        PreviewScheme();
    }

    [RelayCommand]
    private void SelectPreset(string? name) { if (!string.IsNullOrEmpty(name)) ColorScheme = name; }

    [RelayCommand]
    private void ResetSchemeColors()
    {
        foreach (var key in _schemeOverrides.Keys.Where(k => k.StartsWith(EditingVariant + ":", StringComparison.OrdinalIgnoreCase)).ToList())
            _schemeOverrides.Remove(key);
        RebuildSchemeItems();
        PreviewScheme();
    }

    [RelayCommand]
    private void ResetColorScheme()
    {
        _schemeOverrides.Clear();
        ColorScheme = ColorSchemeService.Presets[0].Name;
        RebuildSchemeItems();
        PreviewScheme();
    }

    // ----- Updates -----

    [ObservableProperty] private string updateChannel = "Stable";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUpdateStatus))] private string updateStatus = string.Empty;
    [ObservableProperty] private string? updateUrl;
    [ObservableProperty] private bool hasUpdate;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))] private bool isCheckingUpdate;

    public bool HasUpdateStatus => !string.IsNullOrEmpty(UpdateStatus);

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdates()
    {
        IsCheckingUpdate = true;
        HasUpdate = false;
        UpdateStatus = Locales.Loc.T("Checking for updates…");
        try
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
            var result = await UpdateService.CheckAsync(UpdateChannel, current);
            UpdateUrl = result.Url?.ToString();
            HasUpdate = result.IsNewer;
            UpdateStatus = result switch
            {
                { Failed: true } => Locales.Loc.T("Couldn't check for updates. Check your connection and try again."),
                { IsNewer: true } => Locales.Loc.T("Toucan {0} is available.").Replace("{0}", result.Version, StringComparison.Ordinal),
                { Version: null } => Locales.Loc.T("No releases were found."),
                _ => Locales.Loc.T("You're up to date."),
            };
        }
        finally { IsCheckingUpdate = false; }
    }

    private bool CanCheckForUpdates() => !IsCheckingUpdate;

    [RelayCommand]
    private void OpenUpdate() => PlatformService.OpenUrl(UpdateUrl ?? UpdateService.ReleasesUrl);
}

/// <summary>One theme color in the scheme editor: a hex value that overrides the default while it differs from it.</summary>
public sealed partial class SchemeColorItem : ObservableObject
{
    private readonly Action<SchemeColorItem> _edited;
    private bool _loading;

    public SchemeColorItem(string label, string defaultHex, Action<SchemeColorItem> edited, string storageKey)
    {
        Label = label;
        DefaultHex = defaultHex;
        StorageKey = storageKey;
        _edited = edited;
        hex = defaultHex;
        brush = global::Avalonia.Media.Brush.Parse(defaultHex);
    }

    public string Label { get; }
    public string DefaultHex { get; }
    public string StorageKey { get; }

    [ObservableProperty] private string hex;
    [ObservableProperty] private IBrush brush;
    [ObservableProperty] private bool isValid = true;
    [ObservableProperty] private bool isOverridden;

    public void Load(string? overrideHex)
    {
        _loading = true;
        Hex = overrideHex ?? DefaultHex;
        Update();
        _loading = false;
    }

    partial void OnHexChanged(string value)
    {
        Update();
        if (!_loading) _edited(this);
    }

    private void Update()
    {
        IsValid = ColorSchemeService.TryParseColor(Hex, out var color);
        if (IsValid) Brush = new SolidColorBrush(color);
        IsOverridden = IsValid && !string.Equals(ColorSchemeService.Hex(color), DefaultHex, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Reset() => Hex = DefaultHex;
}
