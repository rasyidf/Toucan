using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Toucan.Views
{
    /// <summary>
    /// Interaction logic for StartScreenCard.xaml
    /// </summary>
    public partial class StartScreenCard : UserControl
    {
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(StartScreenCard));
        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(StartScreenCard));

        public StartScreenCard()
        {
            InitializeComponent();
        }
    }

}
