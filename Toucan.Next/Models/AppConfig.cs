namespace Toucan.Models;

public class AppConfig
{
    // Static or startup values
    public string ConfigurationsFolder { get; set; } = "Toucan\\Configurations";
    public string AppPropertiesFileName { get; set; } = "AppProperties.json";
    public string PrivacyStatement { get; set; } = "https://YourPrivacyUrlGoesHere/";

    // User preferences (runtime)
    public bool AutoSave { get; set; } = true;
    public bool EnableBackup { get; set; } = false;
    public bool AutoCapitalize { get; set; } = true;
    public bool UseCompactLayout { get; set; } = false;
    public bool EnableDevTools { get; set; } = false;
    public string SelectedFontSize { get; set; } = "Normal"; // Small | Normal | Large
    public string SelectedKeyStyle { get; set; } = "dot.notation"; // dot.notation | snake_case | camelCase
}
