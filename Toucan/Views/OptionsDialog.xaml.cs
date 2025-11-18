using Toucan.Core.Models;
using System;
using System.Globalization;
using System.Windows;
using Wpf.Ui.Controls;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Services;

namespace Toucan;

/// <summary>
/// Interaction logic for Options.xaml
/// </summary>
public partial class OptionDialog : FluentWindow
{
    public AppOptions Config { get; private set; }

    private readonly OptionsViewModel vm;

    public OptionDialog(AppOptions importOptions)
    {
        InitializeComponent();

        // prefer to use preference service when available, otherwise create one
        IPreferenceService pref = new PreferenceService();
        vm = new OptionsViewModel(pref);
        vm.AppOptions = importOptions ?? vm.AppOptions;
        vm.CloseAction = (result) =>
        {
            if (result == true)
            {
                Config = vm.AppOptions;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
        };

        DataContext = vm;

        // migrate values to VM if code previously relied on controls
        vm.PageSizeText = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture);
        vm.TruncateSizeText = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture);
        vm.Format = importOptions?.SaveStyle.ToString();
    }
}
