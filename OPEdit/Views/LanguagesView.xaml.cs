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
    /// Interaction logic for LanguagesView.xaml
    /// </summary>
    public partial class LanguagesView : UserControl
    {
        public event RoutedEventHandler NewLanguage;
        public LanguagesView()
        {
            InitializeComponent();
        }

        private void HandleNewLanguage(object sender, RoutedEventArgs e)
        {
            NewLanguage?.Invoke(sender, e);
        }
    }
}
