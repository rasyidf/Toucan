using OPEdit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OPEdit.Views
{
    /// <summary>
    /// Interaction logic for TranslationDetailsView.xaml
    /// </summary>
    public partial class TranslationDetailsView : UserControl
    {
        public event RoutedEventHandler FirstPageClick;
        public event RoutedEventHandler LastPageClick;
        public event RoutedEventHandler PreviousPageClick;
        public event RoutedEventHandler NextPageClick;
        public event RoutedEventHandler ShowAllClick;
        public event RoutedEventHandler UpdateLanguageValue;


        public TranslationDetailsView()
        {
            InitializeComponent();
        }

        private void FirstPage(object sender, RoutedEventArgs e)
        {
            FirstPageClick?.Invoke(this, e);
        }

        private void PreviousPage(object sender, RoutedEventArgs e)
        {
            PreviousPageClick?.Invoke(this, e);
        }

        private void NextPage(object sender, RoutedEventArgs e)
        {
            NextPageClick?.Invoke(this, e);

        }

        private void LastPage(object sender, RoutedEventArgs e)
        {
            LastPageClick?.Invoke(sender, e);
        }

        private void ShowAll(object sender, RoutedEventArgs e)
        {
            ShowAllClick?.Invoke(sender, e);
        }

        private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
        {
            TextBox txtBox = (TextBox)sender;
            LanguageSetting setting = (LanguageSetting)txtBox.Tag;
            setting.Value = txtBox.Text;
        }
    }
}
