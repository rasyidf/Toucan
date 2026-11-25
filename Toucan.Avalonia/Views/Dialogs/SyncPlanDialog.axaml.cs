using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Preview of a sync plan: what comes in, what goes out, and the conflicts to settle. Closes when the user applies or cancels.</summary>
public partial class SyncPlanDialog : DialogWindow
{
    public SyncPlanDialog() => InitializeComponent();

    public SyncPlanDialog(SyncPlanViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
        Tabs.SelectedIndex = vm.StartOnConflicts || (!vm.HasPull && !vm.HasPush && vm.HasConflicts) ? 2 : vm.HasPull || !vm.HasPush ? 0 : 1;
    }
}
