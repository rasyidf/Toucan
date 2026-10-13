using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Toucan.Avalonia.Locales;
using Toucan.Plugins;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// A settings form generated from a plugin's <see cref="ConfigSchema"/>: one row per field with the editor that suits its
/// type, validation on every change (the value is kept only when valid) and a way back to the default. Fields scoped to a
/// project show while one is open; connection fields belong to the plugin's own screens.
/// </summary>
public sealed class ConfigForm : UserControl
{
    private readonly IPluginConfiguration _config;
    private readonly Func<string?> _workspaceId;
    private readonly List<Action> _refreshers = [];

    public ConfigForm(IPluginConfiguration config, Func<string?> workspaceId)
    {
        _config = config;
        _workspaceId = workspaceId;

        var panel = new StackPanel { Spacing = 0 };
        var fields = config.Schema?.Fields ?? [];
        var hasProject = workspaceId() is not null;
        foreach (var field in fields.Where(f => f.Scope == ConfigScope.App || f.Scope == ConfigScope.Workspace && hasProject))
            panel.Children.Add(BuildRow(field));
        if (!hasProject && fields.Any(f => f.Scope == ConfigScope.Workspace))
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Settings for a project appear here while one is open."), Classes = { "caption" },
                Margin = new Thickness(16, 8, 16, 12), TextWrapping = TextWrapping.Wrap,
            });
        Content = panel;
    }

    private ConfigTarget? TargetFor(ConfigField field) => field.Scope == ConfigScope.Workspace ? ConfigTarget.ForWorkspace(_workspaceId()!) : null;

    private Control BuildRow(ConfigField field)
    {
        var error = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 8), IsVisible = false };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("BadBrush"));

        var reset = new Button { Content = Loc.T("Reset"), IsVisible = false };
        var editor = BuildEditor(field, error, out var refresh);
        reset.Click += async (_, _) =>
        {
            await Commit(field, null, error);
            refresh();
        };
        var editors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { editor } };
        if (field.Type != ConfigFieldType.Secret) editors.Children.Add(reset);

        void Refresh()
        {
            refresh();
            reset.IsVisible = field.Type != ConfigFieldType.Secret && !Equals(Current(field), field.Default is long l && field.Type == ConfigFieldType.WholeNumber ? l : field.Default);
        }
        _refreshers.Add(Refresh);
        Refresh();

        var title = LocalizedText.Pick(field.LocalizedLabels, field.Label);
        var row = new SettingsRow { Title = title, Description = field.Description, Content = editors };
        global::Avalonia.Automation.AutomationProperties.SetName(editor, title);
        return new StackPanel { Children = { row, error } };
    }

    private object? Current(ConfigField field)
    {
        var target = TargetFor(field);
        return field.Type switch
        {
            ConfigFieldType.Boolean => _config.GetValue<bool>(field.Key, target),
            ConfigFieldType.WholeNumber => _config.GetValue<long?>(field.Key, target),
            ConfigFieldType.Number => _config.GetValue<double?>(field.Key, target),
            _ => _config.GetValue<string>(field.Key, target),
        };
    }

    private Control BuildEditor(ConfigField field, TextBlock error, out Action refresh)
    {
        var suppress = false;
        switch (field.Type)
        {
            case ConfigFieldType.Boolean:
            {
                var toggle = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty };
                toggle.IsCheckedChanged += async (_, _) =>
                {
                    if (!suppress) await Commit(field, toggle.IsChecked == true, error);
                };
                refresh = () => Set(() => toggle.IsChecked = _config.GetValue<bool>(field.Key, TargetFor(field)));
                return toggle;
            }

            case ConfigFieldType.WholeNumber or ConfigFieldType.Number:
            {
                var whole = field.Type == ConfigFieldType.WholeNumber;
                var box = new NumericUpDown
                {
                    Width = 140, FormatString = whole ? "0" : "0.###", Increment = whole ? 1 : 0.1m,
                    Minimum = field.Minimum is { } min ? (decimal)min : decimal.MinValue,
                    Maximum = field.Maximum is { } max ? (decimal)max : decimal.MaxValue,
                };
                box.ValueChanged += async (_, e) =>
                {
                    if (!suppress && e.NewValue is { } v) await Commit(field, whole ? (long)v : (double)v, error);
                };
                refresh = () => Set(() => box.Value = Current(field) is { } v ? System.Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null);
                return box;
            }

            case ConfigFieldType.Choice:
            {
                var combo = new ComboBox { MinWidth = 160, ItemsSource = field.Choices.Select(c => c.Label).ToList() };
                combo.SelectionChanged += async (_, _) =>
                {
                    if (!suppress && combo.SelectedIndex >= 0) await Commit(field, field.Choices[combo.SelectedIndex].Value, error);
                };
                refresh = () => Set(() => combo.SelectedIndex = field.Choices.ToList().FindIndex(c => c.Value == _config.GetValue<string>(field.Key, TargetFor(field))));
                return combo;
            }

            case ConfigFieldType.Secret:
            {
                var box = new TextBox { Width = 240, PasswordChar = '•' };
                var remove = new Button { Content = Loc.T("Remove"), IsVisible = false };
                async Task RefreshSecret()
                {
                    var stored = !string.IsNullOrEmpty(await _config.GetSecretAsync(field.Key, TargetFor(field)));
                    box.PlaceholderText = stored ? Loc.T("Stored. Type to replace.") : Loc.T("Not set");
                    remove.IsVisible = stored;
                }
                async void Save()
                {
                    if (suppress || string.IsNullOrEmpty(box.Text)) return;
                    if (await Commit(field, box.Text, error)) { suppress = true; box.Text = string.Empty; suppress = false; }
                    await RefreshSecret();
                }
                box.LostFocus += (_, _) => Save();
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
                remove.Click += async (_, _) =>
                {
                    await Commit(field, null, error);
                    await RefreshSecret();
                };
                refresh = () => _ = RefreshSecret();
                return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, remove } };
            }

            default: // Text, Path, Url
            {
                var box = new TextBox { Width = 280 };
                async void Save()
                {
                    if (!suppress) await Commit(field, box.Text ?? string.Empty, error);
                }
                box.LostFocus += (_, _) => Save();
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
                refresh = () => Set(() => box.Text = _config.GetValue<string>(field.Key, TargetFor(field)) ?? string.Empty);
                return box;
            }
        }

        void Set(Action action)
        {
            suppress = true;
            try { action(); }
            finally { suppress = false; }
        }
    }

    /// <summary>Stores a value; an invalid one shows why under the row and leaves the stored value alone.</summary>
    private async Task<bool> Commit(ConfigField field, object? value, TextBlock error)
    {
        var result = await _config.SetAsync(field.Key, value, TargetFor(field));
        error.Text = result.Error;
        error.IsVisible = !result.IsValid;
        foreach (var refresh in _refreshers) refresh();
        return result.IsValid;
    }
}
