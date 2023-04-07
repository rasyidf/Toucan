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
using OPEdit.Views;
using Google.Cloud.Translation.V2;
using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;

namespace OPEdit.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private List<TranslationItem> allTranslation;

    [ObservableProperty]
    private NsTreeItem selectedNode;

    [ObservableProperty]
    private SummaryInfoViewModel summaryInfo = new();
    [ObservableProperty]
    private PagingController<LanguageGroup> pagingController = new(30, new List<LanguageGroup>());

    [ObservableProperty]
    private List<NsTreeItem> currentTreeItems = new();

    [ObservableProperty]
    private AppOptions appOptions;

    [ObservableProperty]
    private string currentPath;


    public IEnumerable<LanguageGroup> PageData { get; private set; }
    public string PageMessage { get; private set; }

    [RelayCommand]
    internal void HelpHomepage()
    {
        // Redirect to Rasyid.dev
        OpenUrl("https://rasyid.dev");
    }

    [RelayCommand]
    internal void HelpAbout()
    {
        AboutDialog ad = new(App.Current.MainWindow);
        ad.ShowDialog();
    }


    [RelayCommand]
    private void NextPage()
    {
        PagingController.NextPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        PagingController.PreviousPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void FirstPage()
    {
        PagingController.MoveFirst();
        PagedUpdates();
    }

    [RelayCommand]
    private void LastPage()
    {
        PagingController.LastPage();
        PagedUpdates();
    }

    private void PagedUpdates()
    {
        PageData = PagingController.PageData;
        PageMessage = PagingController.PageMessage;
    }

    private static void OpenUrl(string url)
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
                url = url.Replace("&", "^&", StringComparison.InvariantCulture);
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
