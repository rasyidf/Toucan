using System;
using System.Globalization;
using Wpf.Ui.Controls;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Services;

namespace Toucan;

/// <summary>
/// Interaction logic for Options.xaml
/// </summary>
public partial class OptionDialog : FluentWindow
{
    public AppOptions Config { get; private set; }

    private readonly OptionsViewModel vm;

    public OptionDialog(AppOptions importOptions, string projectPath = "")
    {
        InitializeComponent();

        // prefer to use preference service when available, otherwise create one
        IPreferenceService pref = new PreferenceService();
        vm = new OptionsViewModel(pref);
        // load project manifest editorConfiguration when available
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
                    var editorCfg = root["editorConfiguration"] as Newtonsoft.Json.Linq.JObject;
                    if (editorCfg != null)
                    {
                        vm.ProjectSaveEmptyTranslations = (editorCfg["save_empty_translations"]?.ToString() ?? "true").Equals("true", StringComparison.InvariantCultureIgnoreCase);
                        vm.ProjectTranslationOrder = (editorCfg["translation_order"]?.ToString() == "primary_language") ? "Primary language" : "Alphabetically sorted";
                        var templates = editorCfg["copy_templates"] as Newtonsoft.Json.Linq.JArray;
                        if (templates != null)
                        {
                            vm.ProjectCopyTemplate1 = templates.Count > 0 ? templates[0]?.ToString() ?? string.Empty : string.Empty;
                            vm.ProjectCopyTemplate2 = templates.Count > 1 ? templates[1]?.ToString() ?? string.Empty : string.Empty;
                            vm.ProjectCopyTemplate3 = templates.Count > 2 ? templates[2]?.ToString() ?? string.Empty : string.Empty;
                        }
                        vm.ProjectPrimaryLanguage = root["primaryLanguage"]?.ToString() ?? string.Empty;
                    }
                }
            }
            catch { }
        }
        vm.AppOptions = importOptions ?? vm.AppOptions;
        vm.CloseAction = (result) =>
        {
            if (result == true)
            {
                Config = vm.AppOptions;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
        };

        DataContext = vm;

        // migrate values to VM if code previously relied on controls
        vm.PageSizeText = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture);
        vm.TruncateSizeText = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture);
        vm.Format = importOptions?.SaveStyle.ToString();
        vm.MaxItemsText = importOptions?.MaxItems.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
