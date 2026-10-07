using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class EditableTableTests
{
    private static (Window Window, EditableTable Table) Show(EditableTable table)
    {
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, table);
    }

    [AvaloniaFact]
    public void KeyValueRows_ShowKeyAndValueCells_AndEditsFlowBack()
    {
        var items = new ObservableCollection<KeyValueItem> { new("endpoint", "https://x", "Webhook URL") };
        var (window, table) = Show(new EditableTable { ItemsSource = items, KeyHeader = "Key", ValueHeader = "Value" });

        var cells = table.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("cell")).ToList();
        Assert.Equal(2, cells.Count);
        Assert.Equal("endpoint", cells[0].Text);
        Assert.Equal("https://x", cells[1].Text);

        cells[1].Text = "https://y";
        Assert.Equal("https://y", items[0].Value);
        window.Close();
    }

    [AvaloniaFact]
    public void ColumnTitles_AreShown_WhenGiven()
    {
        var items = new ObservableCollection<KeyValueItem> { new("a", "b", "") };
        var (window, table) = Show(new EditableTable { ItemsSource = items, KeyHeader = "Key", ValueHeader = "Value" });

        var header = table.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tableHeader"));

        Assert.True(header.IsEffectivelyVisible);
        Assert.Equal(["Key", "Value"], header.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        window.Close();
    }

    [AvaloniaFact]
    public void Remove_RunsTheCommandWithTheRowItem_AndSchemaRowsCannotBeRemoved()
    {
        var custom = new KeyValueItem("mine", "1", "");
        var schema = new KeyValueItem("endpoint", "", "Webhook URL", isSchemaField: true);
        var items = new ObservableCollection<KeyValueItem> { schema, custom };
        object? removed = null;
        var (window, table) = Show(new EditableTable { ItemsSource = items, KeyHeader = "Key", ValueHeader = "Value", RemoveCommand = new RelayCommand<object?>(i => removed = i) });

        var buttons = table.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("icon")).ToList();
        var visible = Assert.Single(buttons, b => b.IsEffectivelyVisible);
        visible.Command!.Execute(visible.CommandParameter);

        Assert.Same(custom, removed);
        window.Close();
    }

    [AvaloniaFact]
    public void ValueOnlyTable_HasNoKeyColumn()
    {
        var items = new ObservableCollection<CopyTemplateItem> { new("t('%1')") };
        var (window, table) = Show(new EditableTable { ItemsSource = items, ShowKey = false, ValueHeader = "Template" });

        var cells = table.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("cell")).ToList();

        Assert.Equal("t('%1')", Assert.Single(cells).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void SecretTable_MasksValues()
    {
        var items = new ObservableCollection<KeyValueItem> { new("api_key", "hunter2", "") };
        var (window, table) = Show(new EditableTable { ItemsSource = items, IsSecret = true });

        var value = table.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("cell")).Last();

        Assert.Equal('•', value.PasswordChar);
        window.Close();
    }
}
