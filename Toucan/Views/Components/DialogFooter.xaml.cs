using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toucan.Views.Components;

public partial class DialogFooter : UserControl
{
    public static readonly DependencyProperty OkCommandProperty =
        DependencyProperty.Register(nameof(OkCommand), typeof(ICommand), typeof(DialogFooter));

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(DialogFooter));

    public static readonly DependencyProperty OkTextProperty =
        DependencyProperty.Register(nameof(OkText), typeof(string), typeof(DialogFooter), new PropertyMetadata("OK"));

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(nameof(CancelText), typeof(string), typeof(DialogFooter), new PropertyMetadata("Cancel"));

    public ICommand? OkCommand { get => (ICommand?)GetValue(OkCommandProperty); set => SetValue(OkCommandProperty, value); }
    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public string OkText { get => (string)GetValue(OkTextProperty); set => SetValue(OkTextProperty, value); }
    public string CancelText { get => (string)GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

    public DialogFooter()
    {
        InitializeComponent();
        OkBtn.SetBinding(Wpf.Ui.Controls.Button.CommandProperty, new System.Windows.Data.Binding(nameof(OkCommand)) { Source = this });
        CancelBtn.SetBinding(Wpf.Ui.Controls.Button.CommandProperty, new System.Windows.Data.Binding(nameof(CancelCommand)) { Source = this });
        OkBtn.SetBinding(ContentProperty, new System.Windows.Data.Binding(nameof(OkText)) { Source = this });
        CancelBtn.SetBinding(ContentProperty, new System.Windows.Data.Binding(nameof(CancelText)) { Source = this });
    }
}
