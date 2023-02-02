using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OPEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string startupPath = "";

            if (e.Args.Length == 1)
            {
                startupPath = e.Args[0];
            }
            MainWindow wnd = new(startupPath, new ViewModels.MainWindowViewModel());
            wnd.Show();

        }
    }
}
