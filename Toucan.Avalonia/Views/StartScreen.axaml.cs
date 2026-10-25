using Avalonia.Controls;

namespace Toucan.Avalonia.Views;

/// <summary>Welcome screen shown when no project is open.</summary>
public partial class StartScreen : UserControl
{
    public StartScreen()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    private bool? _compact;

    private void UpdateLayoutForWidth()
    {
        var compact = Bounds.Width < 1120;
        if (_compact == compact) return;
        _compact = compact;
        WelcomeSections.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "1.55*,1.05*");
        SecondarySections.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "1.7*,1*");
        Grid.SetColumn(RecentProjectsSection, compact ? 0 : 1);
        Grid.SetRow(RecentProjectsSection, compact ? 1 : 0);
        Grid.SetColumn(SecondarySections.Children[1], compact ? 0 : 1);
        Grid.SetRow(SecondarySections.Children[1], compact ? 1 : 0);
    }

    private void OnProjectsClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) =>
        RecentProjectsSection.BringIntoView();
}
