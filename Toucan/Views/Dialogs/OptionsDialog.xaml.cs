using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Services;

namespace Toucan;

public partial class OptionDialog : FluentWindow
{
    public AppOptions Config { get; private set; }
    private readonly OptionsViewModel vm;
    private readonly StackPanel[] _pages;

    public OptionDialog(AppOptions importOptions, string projectPath = "")
    {
        InitializeComponent();

        _pages = [PageGeneral, PageEditor, PageLanguages, PageTranslation, PageShortcuts, PageProject, PageAbout];

        IPreferenceService pref = App.Services?.GetService(typeof(IPreferenceService)) as IPreferenceService ?? new PreferenceService();
        vm = App.Services?.GetService(typeof(OptionsViewModel)) as OptionsViewModel ?? new OptionsViewModel(pref);

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            vm.ProjectFilePath = projectPath;
            try
            {
                var manifestPath = System.IO.Path.Combine(projectPath, "toucan.project");
                if (System.IO.File.Exists(manifestPath))
                {
                    var text = System.IO.File.ReadAllText(manifestPath);
                    var root = Newtonsoft.Json.Linq.JObject.Parse(text);
                    vm.ProjectPrimaryLanguage = root["primaryLanguage"]?.ToString() ?? string.Empty;
                    var editorCfg = root["editorConfiguration"] as Newtonsoft.Json.Linq.JObject;
                    if (editorCfg != null)
                    {
                        vm.ProjectSaveEmptyTranslations = (editorCfg["save_empty_translations"]?.ToString() ?? "true").Equals("true", StringComparison.InvariantCultureIgnoreCase);
                        vm.ProjectTranslationOrder = (editorCfg["translation_order"]?.ToString() ?? "alphabetical") == "primary_language" ? "Primary language" : "Alphabetically sorted";
                        var templates = editorCfg["copy_templates"] as Newtonsoft.Json.Linq.JArray;
                        if (templates != null)
                        {
                            vm.ProjectCopyTemplate1 = templates.Count > 0 ? templates[0]?.ToString() ?? "" : "";
                            vm.ProjectCopyTemplate2 = templates.Count > 1 ? templates[1]?.ToString() ?? "" : "";
                            vm.ProjectCopyTemplate3 = templates.Count > 2 ? templates[2]?.ToString() ?? "" : "";
                        }
                    }
                }
            }
            catch { }
        }

        vm.AppOptions = importOptions ?? vm.AppOptions;
        vm.CloseAction = (result) =>
        {
            if (result == true) { Config = vm.AppOptions; DialogResult = true; }
            else { DialogResult = false; }
        };

        DataContext = vm;
        KeybindingsListView.ItemsSource = KeybindingService.GetDefinitions();

        vm.PageSizeText = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture);
        vm.TruncateSizeText = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture);
        vm.MaxItemsText = importOptions?.MaxItems.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;
        var idx = NavList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddSuggestedLanguage_Click(object sender, RoutedEventArgs e)
    {
        var lang = NewSuggestedLangBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(lang)) return;
        if (vm.SuggestedLanguages.Contains(lang)) return;
        vm.SuggestedLanguages.Add(lang);
        NewSuggestedLangBox.Text = string.Empty;
    }
}
