using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ProjectPropertiesDialog : DialogWindow
{
    public ProjectPropertiesDialog() => InitializeComponent();

    public ProjectPropertiesDialog(ProjectPropertiesViewModel vm, Toucan.Core.Services.DictionaryRegistry? dictionaries = null, Toucan.Avalonia.Services.IDialogService? dialogs = null) : this()
    {
        DataContext = vm;
        if (vm.FrameworkConfiguration != null)
            FrameworkSettingsHost.Content = new Toucan.Avalonia.Views.Components.ConfigForm(vm.FrameworkConfiguration, () => vm.ProjectPath);
        if (vm.ResourceSettings != null) ResourcesHost.Content = new Toucan.Avalonia.Views.Components.ResourceSettingsView(vm.ResourceSettings, dialogs);
        if (vm.ResourceSettings == null) ResourcesHost.Content = new global::Avalonia.Controls.TextBlock { Text = Toucan.Avalonia.Locales.Loc.T("This provider does not expose editable serialization settings."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        if (dictionaries != null && dialogs != null) SpellingHost.Content = new Toucan.Avalonia.Views.Components.SpellingSettingsView(vm.ConfigurationDraft, dictionaries, dialogs);
        vm.CloseAction = ok => Close(ok);
    }
}
