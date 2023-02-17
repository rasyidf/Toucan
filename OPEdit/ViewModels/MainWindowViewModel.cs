using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPEdit.Core.Models;
using OPEdit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OPEdit.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public List<LanguageSetting> allSettings;

        [ObservableProperty]
        public NsTreeItem selectedNode;

        [ObservableProperty]
        public SummaryInfo summaryInfo = new();
        [ObservableProperty]
        public PagingController<LanguageGroup> pagingController = new(30, new List<LanguageGroup>());

        [ObservableProperty]
        public List<NsTreeItem> currentTreeItems = new();

        [ObservableProperty]
        public AppOptions appOptions;

        [ObservableProperty]
        public string currentPath;

        [RelayCommand]
        internal void HelpHomepage()
        {
            // Redirect to Rasyid.dev
            OpenUrl("https://rasyid.dev");
        }

        [RelayCommand]
        internal void HelpAbout()
        {
            MessageBox.Show("OP Editor \r\nVersion : 0.1", "About OP Editor");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

    }
}
