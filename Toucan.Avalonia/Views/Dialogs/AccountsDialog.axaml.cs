using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Accounts the user is signed in with, which projects use each, and sign-in and sign-out.</summary>
public partial class AccountsDialog : DialogWindow
{
    public AccountsDialog() => InitializeComponent();

    public AccountsDialog(AccountsViewModel vm) : this()
    {
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
