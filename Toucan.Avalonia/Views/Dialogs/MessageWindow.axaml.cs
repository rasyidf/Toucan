using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Toucan.Avalonia.Views.Dialogs
{
    public partial class MessageWindow : Window
    {
        public MessageWindow()
        {
            InitializeComponent();
            OkButton.Click += OkButton_Click;
        }

        public MessageWindow(string title, string message) : this()
        {
            TitleBlock.Text = title;
            MessageBlock.Text = message;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
