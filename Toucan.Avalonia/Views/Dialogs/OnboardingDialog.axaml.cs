using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class OnboardingDialog : DialogWindow
{
    private bool _finished;

    public OnboardingDialog() => InitializeComponent();

    public OnboardingDialog(OnboardingViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = ok =>
        {
            _finished = true;
            Close(ok);
        };
        // Closing the window without Continue keeps AI off and still records the choice.
        Closing += (_, _) =>
        {
            if (!_finished) vm.Dismiss();
        };
    }
}
