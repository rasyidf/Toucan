using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class StatisticsDialog : DialogWindow
{
    public StatisticsDialog() => InitializeComponent();

    public StatisticsDialog(StatisticsViewModel vm) : this()
    {
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
