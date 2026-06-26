using System.Reflection;
using Avalonia.Controls;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
        CloseBtn.Click += (_, _) => Close();
    }
}
