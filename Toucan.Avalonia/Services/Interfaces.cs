using System.Threading.Tasks;

namespace Toucan.Avalonia.Services;

public interface IDialogService
{
    Task<string?> SelectFolderAsync(string? initialPath = null);
    Task<string?> SelectFileAsync(string? initialPath = null, string filter = "All Files");
}
