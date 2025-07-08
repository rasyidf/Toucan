using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OPEdit.Avalonia.ViewModels;

namespace OPEdit.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            this.DataContext = new MainWindowViewModel();
            this.AttachDevTools();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
