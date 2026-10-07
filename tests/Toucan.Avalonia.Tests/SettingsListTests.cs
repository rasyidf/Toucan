using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class SettingsListTests
{
    private static SettingsList Create(int count, out Window window)
    {
        var list = new SettingsList
        {
            Header = "Items",
            EmptyText = "Nothing here",
            ItemsSource = Enumerable.Range(1, count).Select(i => $"Item {i}").ToList(),
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<string>((s, _) => new SettingsRow { Title = s }),
        };
        window = new Window { Width = 500, Height = 600, Content = list };
        window.Show();
        Settle();
        return list;
    }

    /// <summary>Layout, container creation and the list's queued refresh each need a dispatcher pass.</summary>
    private static void Settle() { for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void ShortList_HidesSearchAndCapsNothing()
    {
        var list = Create(3, out var window);
        try
        {
            Assert.False(list.IsSearchBarVisible);
            Assert.Equal("3", list.CountText);
            Assert.Null(list.EmptyMessage);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LongList_ShowsSearchFiltersAndScrollsWithinMaxHeight()
    {
        var list = Create(30, out var window);
        try
        {
            Assert.True(list.IsSearchBarVisible);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Bounds.Height <= list.MaxListHeight + 1);

            list.SearchText = "item 2";
            Settle();
            var visible = list.GetVisualDescendants().OfType<SettingsRow>().Where(r => r.IsEffectivelyVisible).Select(r => r.Title).ToList();
            Assert.Equal(12, visible.Count); // Item 2, 12, 20..29
            Assert.Equal("12 of 30", list.CountText);

            list.SearchText = "zzz";
            Settle();
            Assert.Equal("No matches", list.EmptyMessage);

            list.SearchText = "";
            Settle();
            Assert.Null(list.EmptyMessage);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EmptyList_ShowsEmptyText()
    {
        var list = Create(0, out var window);
        try { Assert.Equal("Nothing here", list.EmptyMessage); }
        finally { window.Close(); }
    }
}
