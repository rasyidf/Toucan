using System.Windows.Controls;
using Toucan.Services;

namespace Toucan.Views.Settings;

public partial class ShortcutsSettingsPage : UserControl
{
    public ShortcutsSettingsPage()
    {
        InitializeComponent();
        KeybindingsListView.ItemsSource = KeybindingService.GetDefinitions();
    }
}
