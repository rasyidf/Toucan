using OPEdit.Core.Models;
using OPEdit.Core.Services;
using System;
using System.Globalization;
using System.Windows;
using Wpf.Ui.Controls.Window;

namespace OPEdit
{
    /// <summary>
    /// Interaction logic for Options.xaml
    /// </summary>
    public partial class Options : FluentWindow
    {
        public AppOptions Config { get; private set; }

        public Options(AppOptions importOptions)
        {
            Config = importOptions;

            InitializeComponent();

            SaveStyleCombobox.SelectedIndex = (int)Config.SaveStyle;

            PageSizeText.Text = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture);
            TruncateSizeText.Text = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture);

        }

        private void SaveOptions(object sender, RoutedEventArgs e)
        {
            AppOptions newOptions = new()
            {
                SaveStyle = (SaveStyles)SaveStyleCombobox.SelectedIndex,
                PageSize = Convert.ToInt32(PageSizeText.Text),
                TruncateResultsOver = Convert.ToInt32(TruncateSizeText.Text),
                DefaultPath = Config.DefaultPath
            };
            Config = newOptions;
            newOptions.ToDisk();
            DialogResult = true;
        }

        private void CloseOptions(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
