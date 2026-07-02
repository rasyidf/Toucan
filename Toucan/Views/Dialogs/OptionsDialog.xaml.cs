using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan;

public partial class OptionDialog : FluentWindow
{
    public AppOptions? Config { get; private set; }
    private readonly OptionsViewModel vm;
    private readonly UIElement[] _pages;

    public OptionDialog(AppOptions importOptions, string projectPath, OptionsViewModel viewModel)
    {
        InitializeComponent();

        _pages = [PageGeneral, PageAppearance, PageEditor, PageTranslation, PageShortcuts, PageLanguages, PageIntegration, PageAbout];

        vm = viewModel;

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            vm.ProjectFilePath = projectPath;
            try
            {
                string manifestPath = System.IO.Path.Combine(projectPath, "toucan.tproj");
                if (System.IO.File.Exists(manifestPath))
                {
                    string text = System.IO.File.ReadAllText(manifestPath);
                    var root = JsonNode.Parse(text)?.AsObject();
                    vm.ProjectPrimaryLanguage = root?["primaryLanguage"]?.ToString() ?? string.Empty;
                    var editorCfg = root?["editorConfiguration"]?.AsObject();
                    if (editorCfg != null)
                    {
                        vm.ProjectSaveEmptyTranslations = (editorCfg["save_empty_translations"]?.ToString() ?? "true").Equals("true", StringComparison.InvariantCultureIgnoreCase);
                        vm.ProjectTranslationOrder = (editorCfg["translation_order"]?.ToString() ?? "alphabetical") == "primary_language" ? "Primary language" : "Alphabetically sorted";
                        var templates = editorCfg["copy_templates"]?.AsArray();
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

        vm.PageSizeText = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        vm.TruncateSizeText = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        vm.MaxItemsText = importOptions?.MaxItems.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null)
        {
            return;
        }

        int idx = NavList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
        }
    }

}
