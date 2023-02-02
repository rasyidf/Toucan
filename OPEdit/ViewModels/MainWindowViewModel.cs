using CommunityToolkit.Mvvm.ComponentModel;
using OPEdit.Core.Models;
using OPEdit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPEditor.ViewModels
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




    }
}
