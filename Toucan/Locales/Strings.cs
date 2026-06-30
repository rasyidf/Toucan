using System.Globalization;
using System.Resources;

namespace Toucan.Locales;

/// <summary>
/// Strongly-typed accessor for localized UI strings.
/// Reads from Strings.resx / Strings.{culture}.resx embedded resources.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager s_rm = new("Toucan.Locales.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => s_rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    // ═══ Menu ═══
    public static string Menu_File => Get(nameof(Menu_File));
    public static string Menu_Edit => Get(nameof(Menu_Edit));
    public static string Menu_Find => Get(nameof(Menu_Find));
    public static string Menu_Views => Get(nameof(Menu_Views));
    public static string Menu_Help => Get(nameof(Menu_Help));

    // ═══ File menu ═══
    public static string File_NewProject => Get(nameof(File_NewProject));
    public static string File_OpenFolder => Get(nameof(File_OpenFolder));
    public static string File_Save => Get(nameof(File_Save));
    public static string File_SaveAs => Get(nameof(File_SaveAs));
    public static string File_Exit => Get(nameof(File_Exit));
    public static string File_Preferences => Get(nameof(File_Preferences));

    // ═══ Panels ═══
    public static string Panel_Inspector => Get(nameof(Panel_Inspector));
    public static string Panel_Languages => Get(nameof(Panel_Languages));
    public static string Panel_TranslationIDs => Get(nameof(Panel_TranslationIDs));

    // ═══ Inspector tabs ═══
    public static string Inspector_Stats => Get(nameof(Inspector_Stats));
    public static string Inspector_Suggestions => Get(nameof(Inspector_Suggestions));
    public static string Inspector_Details => Get(nameof(Inspector_Details));
    public static string Inspector_SelectKey => Get(nameof(Inspector_SelectKey));
    public static string Inspector_SelectKeySuggestions => Get(nameof(Inspector_SelectKeySuggestions));
    public static string Inspector_NoSuggestions => Get(nameof(Inspector_NoSuggestions));

    // ═══ Editor ═══
    public static string Editor_FilterPlaceholder => Get(nameof(Editor_FilterPlaceholder));
    public static string Editor_ShowAll => Get(nameof(Editor_ShowAll));

    // ═══ Toolbar tooltips ═══
    public static string Tooltip_NewProject => Get(nameof(Tooltip_NewProject));
    public static string Tooltip_OpenProject => Get(nameof(Tooltip_OpenProject));
    public static string Tooltip_Save => Get(nameof(Tooltip_Save));
    public static string Tooltip_ToggleSidebar => Get(nameof(Tooltip_ToggleSidebar));
    public static string Tooltip_ToggleInspector => Get(nameof(Tooltip_ToggleInspector));

    // ═══ Options ═══
    public static string Options_General => Get(nameof(Options_General));
    public static string Options_DefaultLanguage => Get(nameof(Options_DefaultLanguage));
    public static string Options_DefaultLanguageDesc => Get(nameof(Options_DefaultLanguageDesc));
    public static string Options_Theme => Get(nameof(Options_Theme));
    public static string Options_ThemeDesc => Get(nameof(Options_ThemeDesc));
    public static string Options_PageSize => Get(nameof(Options_PageSize));
    public static string Options_PageSizeDesc => Get(nameof(Options_PageSizeDesc));
    public static string Options_AppLanguage => Get(nameof(Options_AppLanguage));
    public static string Options_AppLanguageDesc => Get(nameof(Options_AppLanguageDesc));

    // ═══ Status messages ═══
    public static string Status_Ready => Get(nameof(Status_Ready));
    public static string Status_Loading => Get(nameof(Status_Loading));
    public static string Status_PreTranslateComplete => Get(nameof(Status_PreTranslateComplete));
    public static string Status_NoTranslationsLoaded => Get(nameof(Status_NoTranslationsLoaded));

    // ═══ Mode selector ═══
    public static string Mode_Edit => Get(nameof(Mode_Edit));
    public static string Mode_Review => Get(nameof(Mode_Review));
    public static string Mode_Audit => Get(nameof(Mode_Audit));

    // ═══ Language status ═══
    public static string LangStatus_Translated => Get(nameof(LangStatus_Translated));
    public static string LangStatus_Empty => Get(nameof(LangStatus_Empty));
    public static string LangStatus_NeedsReview => Get(nameof(LangStatus_NeedsReview));
    public static string LangStatus_Approved => Get(nameof(LangStatus_Approved));
}
