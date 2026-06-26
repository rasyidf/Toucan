using System.Threading.Tasks;

namespace Toucan.Avalonia.Services;

public interface IDialogService
{
    Task<string?> SelectFolderAsync(string? initialPath = null);
    Task<string?> SelectFileAsync(string? initialPath = null, string filter = "All Files");
}

public interface IMessageService
{
    void ShowMessage(string message, string title = "Info");
    bool ShowConfirmation(string message, string title = "Confirm");
    Task ShowMessageAsync(string message, string title = "Info");
    Task<bool> ShowConfirmationAsync(string message, string title = "Confirm");
}

public interface IPreferenceService
{
    Toucan.Core.Options.AppOptions Load();
    void Save(Toucan.Core.Options.AppOptions options);
}

public interface ISecureStorageService
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

public interface IProviderSettingsService
{
    System.Collections.Generic.IEnumerable<Toucan.Core.Models.ProviderSettings> LoadAppProviderSettings();
    void SaveAppProviderSettings(System.Collections.Generic.IEnumerable<Toucan.Core.Models.ProviderSettings> settings);
    System.Collections.Generic.IEnumerable<Toucan.Core.Models.ProviderSettings> LoadProjectProviderSettings(string projectPath);
    void SaveProjectProviderSettings(string projectPath, System.Collections.Generic.IEnumerable<Toucan.Core.Models.ProviderSettings> settings);
}
