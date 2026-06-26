using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Toucan.Avalonia.Views.Dialogs
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow()
        {
            InitializeComponent();
            YesButton.Click += YesButton_Click;
            NoButton.Click += NoButton_Click;
        }

        public ConfirmWindow(string title, string message) : this()
        {
            TitleBlock.Text = title;
            MessageBlock.Text = message;
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);
        private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);
    }
}
