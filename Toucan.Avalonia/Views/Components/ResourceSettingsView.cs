using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Detached resource editor. Only providers declaring a setting expose an editor.</summary>
public sealed class ResourceSettingsView : UserControl
{
    private readonly ResourceSettingsViewModel _model;
    private readonly StackPanel _details = new() { Spacing = 8 };
    private readonly ListBox _resources;
    private readonly TextBlock _result = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    public ResourceSettingsView(ResourceSettingsViewModel model, Toucan.Avalonia.Services.IDialogService? dialogs = null)
    {
        _model = model;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = Loc.T("Resources"), FontSize = 20 });
        body.Children.Add(new TextBlock { Text = Loc.T("Changes are staged until Save. Detaching or assigning a new destination retains the original file."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        _resources = new ListBox { ItemsSource = model.Resources, DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"), MaxHeight = 150 };
        global::Avalonia.Automation.AutomationProperties.SetName(_resources, Loc.T("Resources"));
        _resources.SelectionChanged += (_, _) => ShowResource(); body.Children.Add(_resources);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var formats = new ComboBox { ItemsSource = model.TargetFormats.ToList(), DisplayMemberBinding = new global::Avalonia.Data.Binding("DisplayName"), SelectedIndex = 0, MaxWidth = 240 };
        global::Avalonia.Automation.AutomationProperties.SetName(formats, Loc.T("File format"));
        buttons.Children.Add(formats);
        var add = new Button { Content = Loc.T("Add resource") }; add.Click += (_, _) => { if (formats.SelectedItem is Toucan.Core.Contracts.Services.ISaveStrategy f) _resources.SelectedItem = model.AddTarget(f.FormatId); }; buttons.Children.Add(add);
        var detach = new Button { Content = Loc.T("Detach") }; detach.Click += (_, _) => { if (_resources.SelectedItem is ResourceMappingDraft row && !model.IsProtected(row)) model.Resources.Remove(row); }; buttons.Children.Add(detach);
        var convert = new Button { Content = Loc.T("Convert to selected format") };
        convert.Click += (_, _) => { try { if (_resources.SelectedItem is ResourceMappingDraft row && formats.SelectedItem is Toucan.Core.Contracts.Services.ISaveStrategy f) _resources.SelectedItem = model.Convert(row, f.FormatId); } catch (Exception ex) when (ex is FormatException or InvalidOperationException) { _result.Text = ex.Message; } };
        buttons.Children.Add(convert);
        var copyExternal = new Button { Content = Loc.T("Copy external resource…"), IsEnabled = dialogs != null };
        copyExternal.Click += async (_, _) =>
        {
            if (dialogs == null || formats.SelectedItem is not Toucan.Core.Contracts.Services.ISaveStrategy format) return;
            var path = await dialogs.SelectFileAsync(null, Loc.T("Select a resource to copy"));
            if (path != null) _resources.SelectedItem = model.CopyExternal(format.FormatId, path);
        };
        body.Children.Add(copyExternal);
        body.Children.Add(buttons); body.Children.Add(_details);
        var defaults = new Expander { Header = Loc.T("Project serialization defaults"), Content = Options(null), IsExpanded = false }; body.Children.Add(defaults);
        var preview = new Button { Content = Loc.T("Preview serialized files") }; preview.Click += (_, _) => { try { var plan = model.Preview(); _result.Text = string.Join("\n", plan.Diagnostics.Select(d => $"{d.Code}: {d.RelativePath} {d.Message}").Concat(plan.Preview)); } catch (Exception ex) when (ex is FormatException or IOException or InvalidOperationException) { _result.Text = ex.Message; } }; body.Children.Add(preview);
        body.Children.Add(new ScrollViewer { Content = _result, MaxHeight = 260 });
        if (model.Resources.Count == 0) body.Children.Add(new TextBlock { Text = Loc.T("This legacy folder project has no exact resource mappings. Existing folder format preferences still apply."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        Content = body; if (model.Resources.Count > 0) _resources.SelectedIndex = 0;
    }
    private void ShowResource()
    {
        _details.Children.Clear();
        if (_resources.SelectedItem is not ResourceMappingDraft row) return;
        var resource = row.Original.Resource;
        _details.Children.Add(new SelectableTextBlock { Text = $"ID: {resource.Id}\n{resource.FormatId} / {resource.Dialect ?? "default"} / {resource.Ownership}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        if (_model.IsProtected(row)) _details.Children.Add(new TextBlock { Text = Loc.T("Generated from source. Edit the source and extract it again."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        if (!_model.IsExisting(row) && _model.Factory.GetResourceSaveStrategy(resource.FormatId) is Toucan.Core.Contracts.Services.IResourceDialectProvider dialects)
        {
            var choices = dialects.Dialects.Select(d => new DialectChoice(d.Id, Loc.T(d.Label))).ToList();
            var choice = new ComboBox { ItemsSource = choices, DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"), SelectedItem = choices.FirstOrDefault(d => d.Id == row.Dialect), HorizontalAlignment = HorizontalAlignment.Stretch };
            choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is DialectChoice dialect) { row.Dialect = dialect.Id; if (dialect.Id == "formatjs-target") row.Options = row.Options with { Layout = ResourceLayout.Literal }; ShowResource(); } };
            global::Avalonia.Automation.AutomationProperties.SetName(choice, Loc.T("Resource dialect"));
            _details.Children.Add(new TextBlock { Text = Loc.T("Resource dialect") }); _details.Children.Add(choice);
        }
        Edit("File path", row.RelativePath, value => row.RelativePath = value);
        Edit("Package", row.Package, value => row.Package = value);
        Edit("Locale", row.Language, value => row.Language = value);
        Edit("Display prefix", row.SelectorPrefix, value => row.SelectorPrefix = value);
        var reload = new CheckBox { Content = Loc.T("Reload input using selected encoding (save existing edits first)"), IsChecked = row.ReloadInput, IsEnabled = !_model.IsProtected(row) };
        reload.IsCheckedChanged += (_, _) => row.ReloadInput = reload.IsChecked == true; _details.Children.Add(reload);
        _details.Children.Add(Options(row));
        void Edit(string label, string value, Action<string> set)
        {
            var box = new TextBox { Text = value, IsReadOnly = _model.IsProtected(row) };
            global::Avalonia.Automation.AutomationProperties.SetName(box, Loc.T(label));
            box.TextChanged += (_, _) => set(box.Text ?? "");
            _details.Children.Add(new TextBlock { Text = Loc.T(label) }); _details.Children.Add(box);
        }
    }
    private sealed record DialectChoice(string? Id, string Label);
    private sealed record OptionChoice(object? Value, string Label);
    private static string FieldLabel(string field) => field switch
    {
        "InputEncoding" => "Input encoding", "OutputEncoding" => "Output encoding", "SaveEmptyTranslations" => "Write empty translations",
        "UnicodeEscapes" => "Unicode escapes", "Indent" => "Indentation", "Order" => "Key order", "Layout" => "Key layout",
        "Delimiter" => "Key separator", "Arrays" => "Array handling", "Newline" => "Line endings", _ => field
    };
    private static string ValueLabel(string value) => value switch
    {
        "Literal" => "Literal keys", "Dotted" => "Dotted keys", "Nested" => "Nested keys", "Preserve" => "Preserve document setting",
        "Disabled" => "No arrays", "ContiguousIndexes" => "Convert contiguous numeric indexes", "None" => "Compact",
        "Space2" => "2 spaces", "Space4" => "4 spaces", "Tab" => "Tabs", "Always" => "Always escape Unicode", "Minimal" => "Minimal escaping",
        "Alphabetical" => "Alphabetical", "Lf" => "LF", "CrLf" => "CRLF", _ => value
    };
    private Control Options(ResourceMappingDraft? row)
    {
        var panel = new StackPanel { Spacing = 6 };
        var fields = row != null ? _model.Fields(row) : _model.Resources.SelectMany(r => _model.Fields(r)).ToHashSet();
        foreach (var property in typeof(ResourceOptions).GetProperties().Where(p => fields.Contains(p.Name)))
        {
            panel.Children.Add(new TextBlock { Text = Loc.T(FieldLabel(property.Name)) });
            var value = property.GetValue(Current());
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type == typeof(string) && property.Name == "Delimiter")
            {
                var box = new TextBox { Text = value as string ?? "", PlaceholderText = Loc.T("Inherit") };
                global::Avalonia.Automation.AutomationProperties.SetName(box, Loc.T(FieldLabel(property.Name)));
                box.TextChanged += (_, _) => Set(property, string.IsNullOrEmpty(box.Text) ? null : box.Text); panel.Children.Add(box);
            }
            else
            {
                var choices = new List<OptionChoice> { new(null, Loc.T("Inherit")) };
                if (type.IsEnum) choices.AddRange(Enum.GetValues(type).Cast<object>().Select(v => new OptionChoice(v, Loc.T(ValueLabel(v.ToString()!)))));
                else if (type == typeof(bool)) choices.AddRange([new(true, Loc.T("Yes")), new(false, Loc.T("No"))]);
                else choices.AddRange(new[] { "UTF-8", "UTF-8 BOM", "ISO-8859-1" }.Select(v => new OptionChoice(v, v)));
                var choice = new ComboBox { ItemsSource = choices, DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"), SelectedItem = choices.FirstOrDefault(c => Equals(c.Value, value)) ?? choices[0], HorizontalAlignment = HorizontalAlignment.Stretch };
                global::Avalonia.Automation.AutomationProperties.SetName(choice, Loc.T(FieldLabel(property.Name)));
                choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is OptionChoice selected) Set(property, selected.Value); };
                panel.Children.Add(choice);
            }
            var effective = _model.Effective(row);
            var origin = row != null && property.GetValue(row.Options) != null ? "resource" : property.GetValue(_model.Defaults) != null ? "project" : row != null && property.GetValue(row.Original.DetectedOptions) != null ? "detected" : "format default";
            var effectiveLabel = new TextBlock { Opacity = .7 };
            void Refresh(object? sender, EventArgs args)
            {
                var effectiveOptions = _model.Effective(row);
                var source = _model.Origin(row, property.Name);
                effectiveLabel.Text = $"{Loc.T("Effective")}: {property.GetValue(effectiveOptions) ?? Loc.T("Format default")} ({Loc.T(source)})";
            }
            _model.OptionsChanged += Refresh; Refresh(null, EventArgs.Empty); panel.Children.Add(effectiveLabel);
        }
        if (fields.Count == 0) panel.Children.Add(new TextBlock { Text = Loc.T("This provider does not expose editable serialization settings.") });
        return panel;
        ResourceOptions Current() => row?.Options ?? _model.Defaults;
        void Set(PropertyInfo property, object? value)
        {
            var options = Current() with { }; property.SetValue(options, value);
            if (row == null) _model.Defaults = options; else row.Options = options;
            _model.RefreshEffectiveOptions();
        }
    }
}
