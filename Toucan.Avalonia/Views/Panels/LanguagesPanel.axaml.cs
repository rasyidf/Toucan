using Avalonia.Controls;
using Avalonia.Interactivity;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Views.Panels;

/// <summary>Per-language progress with quick filters and actions.</summary>
public partial class LanguagesPanel : UserControl
{
    public LanguagesPanel() => InitializeComponent();

    private void OnLanguageActions(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SummaryItem item } button || DataContext is not MainWindowViewModel vm) return;
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = "Fill Empty Values (Machine Translation)", Command = vm.PreTranslateLanguageCommand, CommandParameter = item });
        flyout.Items.Add(new MenuItem { Header = "Approve All Translated", Command = vm.ApproveAllForLanguageCommand, CommandParameter = item });
        flyout.Items.Add(new MenuItem { Header = "Make Primary Language", Command = vm.MakePrimaryLanguageCommand, CommandParameter = item });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = "Delete Language…", Command = vm.DeleteLanguageCommand, CommandParameter = item });
        flyout.ShowAt(button);
    }
}
