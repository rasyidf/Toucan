using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// "Open from a service": pick a service, an account and one of its projects, choose a local folder, and Toucan pulls the project
/// there. Reuses the sign-in flow and project discovery of the add-connection wizard.
/// </summary>
public sealed partial class OpenFromServiceViewModel : ObservableObject
{
    private readonly SourceSignInService _signIn;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly IDialogService _dialogs;
    private readonly IAsyncMessageService _messages;
    private readonly SourceSignInFlow _flow;
    private readonly string _rootFolder;
    private bool _folderEdited;

    public OpenFromServiceViewModel(SourceSignInService signIn, ISourceAdapterCatalog adapters, IDialogService dialogs, IAsyncMessageService messages, string? rootFolder = null)
    {
        (_signIn, _adapters, _dialogs, _messages) = (signIn, adapters, dialogs, messages);
        _flow = new SourceSignInFlow(signIn, dialogs, messages);
        _rootFolder = rootFolder ?? Path.Combine(Toucan.Core.Services.UserDataFolder.Root, "Toucan", "remote");
        Services = [.. adapters.Adapters.Where(a => a.Descriptor.Capabilities.HasFlag(SourceCapabilities.ReadDocuments)
            && a.Descriptor.Capabilities.HasFlag(SourceCapabilities.ProjectDiscovery) && a.Descriptor.AuthMethods.Any(m => m is not NoSignInAuth))];
        SelectedService = Services.FirstOrDefault();
    }

    public IReadOnlyList<RegisteredSourceAdapter> Services { get; }
    public ObservableCollection<SourceAccount> Accounts { get; } = [];
    public ObservableCollection<RemoteProjectSummary> Projects { get; } = [];

    [ObservableProperty] private RegisteredSourceAdapter? selectedService;
    [ObservableProperty] private SourceAccount? selectedAccount;
    [ObservableProperty] private RemoteProjectSummary? selectedProject;
    [ObservableProperty] private string folder = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string problem = string.Empty;
    [ObservableProperty] private bool isBusy;

    /// <summary>Set when the project was created; the dialog closes after that.</summary>
    public RemoteProjectResult? Result { get; private set; }

    /// <summary>Makes the project: given the service's adapter, address, project and folder. Supplied by the host.</summary>
    public Func<RegisteredSourceAdapter, SourceAccount, RemoteProjectSummary, string, IProgress<SyncProgress>, CancellationToken, Task<RemoteProjectResult>>? Create { get; set; }

    public bool HasServices => Services.Count > 0;
    public bool HasNoServices => Services.Count == 0;
    public bool HasProblem => Problem.Length > 0;
    public bool CanOpen => !IsBusy && SelectedAccount is not null && SelectedProject is not null && !string.IsNullOrWhiteSpace(Folder);

    public event EventHandler? CloseRequested;

    partial void OnSelectedServiceChanged(RegisteredSourceAdapter? value)
    {
        Accounts.Clear();
        Projects.Clear();
        if (value is null) return;
        foreach (var account in _signIn.Accounts.Accounts.Where(a => a.Adapter == value.Descriptor.Id)) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault();
    }

    partial void OnSelectedAccountChanged(SourceAccount? value)
    {
        OnPropertyChanged(nameof(CanOpen));
        if (value is not null) _ = LoadProjectsAsync();
    }

    partial void OnSelectedProjectChanged(RemoteProjectSummary? value)
    {
        if (value is not null && !_folderEdited && SelectedService is { } service)
            SetFolderFromName(service, value.Name);
        OnPropertyChanged(nameof(CanOpen));
    }

    partial void OnFolderChanged(string value) => OnPropertyChanged(nameof(CanOpen));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanOpen));
    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    private void SetFolderFromName(RegisteredSourceAdapter service, string projectName)
    {
        // The default, which the user may replace: Documents/Toucan/remote/<service>/<project>.
        var stem = RemoteProjectBootstrap.LabelFor(service, projectName).Split(':')[0];
        Folder = Path.Combine(_rootFolder, stem, RemoteProjectBootstrap.FolderName(projectName));
        _folderEdited = false;
    }

    /// <summary>The user typed or chose a folder: stop following the project name.</summary>
    public void FolderChangedByUser(string folder)
    {
        _folderEdited = true;
        Folder = folder;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (SelectedService is not { } service) return;
        var methods = service.Descriptor.AuthMethods.Where(m => m is not NoSignInAuth).ToList();
        var method = methods.Count == 1 ? methods[0] : null;
        if (method is null)
        {
            var label = await _dialogs.ShowPickAsync("Sign in", "How do you want to sign in?", [.. methods.Select(m => m.Label)]);
            method = methods.FirstOrDefault(m => m.Label == label);
        }
        if (method is null) return;
        Uri? address = null;
        if (service.Descriptor.AllowsCustomBaseUrl)
        {
            var text = await _dialogs.ShowPromptAsync("Server address", $"Address of the {service.Descriptor.DisplayName} server (leave the default for the public service).", service.Descriptor.DefaultBaseUrl?.AbsoluteUri ?? "https://");
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out address)) { Problem = "That is not a valid address."; return; }
        }
        var account = await _flow.RunAsync(service, address, method);
        if (account is null) return;
        Accounts.Clear();
        foreach (var a in _signIn.Accounts.Accounts.Where(a => a.Adapter == service.Descriptor.Id)) Accounts.Add(a);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == account.Id) ?? account;
    }

    [RelayCommand]
    private async Task Refresh() => await LoadProjectsAsync();

    private async Task LoadProjectsAsync()
    {
        Projects.Clear();
        Problem = string.Empty;
        if (SelectedAccount is not { } account) return;
        StatusText = "Loading projects…";
        try
        {
            await foreach (var project in _signIn.ListProjectsAsync(account)) Projects.Add(project);
            SelectedProject = Projects.FirstOrDefault();
            if (Projects.Count == 0) Problem = "This account has no projects here.";
        }
        catch (SourceException ex)
        {
            Problem = _signIn.DiagnosticsFor(_signIn.Adapter(account.Adapter)).Redact(ex.Error.Message);
        }
        finally { StatusText = string.Empty; }
    }

    [RelayCommand]
    private async Task Browse()
    {
        var chosen = await _dialogs.SelectFolderAsync(Folder, "Where should the project go?");
        if (!string.IsNullOrEmpty(chosen)) FolderChangedByUser(chosen);
    }

    [RelayCommand]
    private async Task Open()
    {
        if (SelectedService is not { } service || SelectedAccount is not { } account || SelectedProject is not { } project || Create is null) return;
        Problem = string.Empty;
        IsBusy = true;
        try
        {
            var progress = new Progress<SyncProgress>(p => StatusText = p.Message);
            Result = await Create(service, account, project, Folder.Trim(), progress, CancellationToken.None);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is SourceException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            Problem = _signIn.DiagnosticsFor(service).Redact(ex is SourceException s ? s.Error.Message : ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusText = string.Empty;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
