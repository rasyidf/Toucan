using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Detached structured fields and an advanced JSON view. Apply is one metadata history action.</summary>
public sealed class ArbPlaceholderEditor : UserControl
{
    private string _expected = "{}";
    private readonly List<Row> _rows = [];
    private readonly StackPanel _fields = new() { Spacing = 6 };
    private readonly TextBox _json = new() { AcceptsReturn = true, MinHeight = 80, MaxHeight = 200, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    public ArbPlaceholderEditor()
    {
        global::Avalonia.Automation.AutomationProperties.SetName(_json, Loc.T("Advanced placeholder JSON"));
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(_fields);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Button Add(string label, Action action) { var button = new Button { Content = Loc.T(label) }; button.Click += (_, _) => action(); actions.Children.Add(button); return button; }
        Add("Add placeholder", () => { _rows.Add(new Row("name" + (_rows.Count + 1), new JsonObject())); Render(); });
        Add("Apply placeholders", () => ApplyStructured());
        Add("Reload", Reload);
        body.Children.Add(actions);
        body.Children.Add(new Expander { Header = Loc.T("Advanced placeholder JSON"), Content = _json });
        var applyJson = new Button { Content = Loc.T("Apply JSON") }; applyJson.Click += (_, _) => Apply(_json.Text ?? "{}"); body.Children.Add(applyJson);
        body.Children.Add(_status); Content = body;
        DataContextChanged += (_, _) => Reload();
    }
    private sealed record Row(string OriginalName, JsonObject Metadata)
    {
        public TextBox Name { get; } = new() { Text = OriginalName, MinWidth = 80, PlaceholderText = Loc.T("Name") };
        public TextBox Type { get; } = new() { Text = Metadata["type"]?.GetValue<string>() ?? "", MinWidth = 80, PlaceholderText = Loc.T("Type") };
        public TextBox Example { get; } = new() { Text = Metadata["example"]?.ToString() ?? "", MinWidth = 80, PlaceholderText = Loc.T("Example") };
    }
    private void Reload()
    {
        if (DataContext is not TranslationItemViewModel item) return;
        _expected = item.PlaceholderMetadata; _json.Text = _expected; _rows.Clear();
        try
        {
            ArbPlaceholderMetadata.Validate(_expected);
            foreach (var pair in (JsonObject)JsonNode.Parse(_expected)!) _rows.Add(new(pair.Key, (JsonObject)pair.Value!.DeepClone()));
            _status.Text = ArbPlaceholderMetadata.Mismatches(item.Value, _expected);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException) { _status.Text = ex.Message; }
        Render();
    }
    private void Render()
    {
        _fields.Children.Clear();
        foreach (var row in _rows)
        {
            global::Avalonia.Automation.AutomationProperties.SetName(row.Name, Loc.T("Placeholder name"));
            global::Avalonia.Automation.AutomationProperties.SetName(row.Type, Loc.T("Placeholder type"));
            global::Avalonia.Automation.AutomationProperties.SetName(row.Example, Loc.T("Placeholder example"));
            var line = new StackPanel { Spacing = 4 };
            line.Children.Add(row.Name); line.Children.Add(row.Type); line.Children.Add(row.Example);
            var remove = new Button { Content = Loc.T("Remove placeholder") }; remove.Click += (_, _) => { _rows.Remove(row); Render(); }; line.Children.Add(remove);
            _fields.Children.Add(line);
        }
    }
    private void ApplyStructured()
    {
        var json = new JsonObject();
        foreach (var row in _rows)
        {
            var name = row.Name.Text ?? "";
            if (json.ContainsKey(name)) { _status.Text = Loc.T("Placeholder names must be unique."); return; }
            var metadata = (JsonObject)row.Metadata.DeepClone();
            if (string.IsNullOrEmpty(row.Type.Text)) metadata.Remove("type"); else metadata["type"] = row.Type.Text;
            if (string.IsNullOrEmpty(row.Example.Text)) metadata.Remove("example");
            else if (row.Example.Text != row.Metadata["example"]?.ToString()) metadata["example"] = row.Example.Text;
            json[name] = metadata;
        }
        Apply(json.ToJsonString());
    }
    private void Apply(string json)
    {
        if (DataContext is not TranslationItemViewModel item) return;
        var error = item.ApplyPlaceholderMetadata(json, _expected);
        if (error != null) _status.Text = error; else Reload();
    }
}
