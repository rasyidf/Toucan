using OPEdit.Core.Models;
using OPEdit.Core.Services;
using System;
using System.Windows;
using Wpf.Ui.Controls.Window;

namespace OPEditor
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


            if (Config.SaveStyle == SaveStyles.Json)
                JsonRadio.IsChecked = true;
            else
                NamespaceRadio.IsChecked = true;

            PageSizeText.Text = importOptions.PageSize.ToString();
            TruncateSizeText.Text = importOptions.TruncateResultsOver.ToString();

        }

        private void SaveOptions(object sender, RoutedEventArgs e)
        {
            AppOptions newOptions = new();
            if (JsonRadio.IsChecked.GetValueOrDefault())
            {
                newOptions.SaveStyle = SaveStyles.Json;
            }
            else
                newOptions.SaveStyle = SaveStyles.Namespaced;

            newOptions.PageSize = Convert.ToInt32(PageSizeText.Text);
            newOptions.TruncateResultsOver = Convert.ToInt32(TruncateSizeText.Text);

            newOptions.DefaultPath = Config.DefaultPath;
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
