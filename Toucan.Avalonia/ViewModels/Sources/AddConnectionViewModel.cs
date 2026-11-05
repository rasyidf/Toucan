using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Avalonia.ViewModels;

public enum ConnectionStep
{
    Service,
    Account,
    Project,
    Mapping,
    Policy,
    Test,
}

public sealed partial class LanguageMapRowViewModel(string projectLanguage, string? remote, bool isExact) : ObservableObject
{
    public string ProjectLanguage { get; } = projectLanguage;
    public bool IsExact { get; } = isExact;
    public string Hint => IsExact ? string.Empty : Remote is null ? "No match" : "Suggested — please check";

    [ObservableProperty] private string? remote = remote;
}

public sealed partial class ResourceMapRowViewModel(string remoteId, string name) : ObservableObject
{
    public string RemoteId { get; } = remoteId;
    public string Name { get; } = name;

    [ObservableProperty] private bool include = true;
    [ObservableProperty] private string? package;
}

public sealed partial class SettingFieldViewModel(ConfigField field, string? value) : ObservableObject
{
    public ConfigField Field { get; } = field;
    public string Label => Field.Label;
    public bool IsPath => Field.Type == ConfigFieldType.Path;

    [ObservableProperty] private string text = value ?? string.Empty;
}

/// <summary>
/// The add-connection wizard (design 8): service → account and sign-in → remote project → mapping with suggestions and
/// collision checks → role and policy → test → optional first pull. Editing an existing connection starts at the mapping.
/// </summary>
public sealed partial class AddConnectionViewModel : ObservableObject
{
    private readonly SourceSignInService _signIn;
    private readonly ISourceConnectionService _connections;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly IDialogService _dialogs;
    private readonly IAsyncMessageService _messages;
    private readonly SourceSignInFlow _flow;
    private readonly IList<SourceConnectionSettings> _projectConnections;
    private readonly IReadOnlyList<string> _projectLanguages;
    private SourceConnectionSettings _draft = new();
    private bool _added;
    private SourceProjectInfo? _remote;

    public AddConnectionViewModel(SourceSignInService signIn, ISourceConnectionService connections, ISourceAdapterCatalog adapters, IDialogService dialogs,
        IAsyncMessageService messages, IList<SourceConnectionSettings> projectConnections, IReadOnlyList<string> projectLanguages)
    {
        (_signIn, _connections, _adapters, _dialogs, _messages, _projectConnections, _projectLanguages) = (signIn, connections, adapters, dialogs, messages, projectConnections, projectLanguages);
        _flow = new SourceSignInFlow(signIn, dialogs, messages);
        Services = [.. adapters.Adapters];
        SelectedService = Services.FirstOrDefault();
    }

    public IReadOnlyList<RegisteredSourceAdapter> Services { get; }
    public ObservableCollection<SourceAccount> Accounts { get; } = [];
    public ObservableCollection<RemoteProjectSummary> Projects { get; } = [];
    public ObservableCollection<LanguageMapRowViewModel> Languages { get; } = [];
    public ObservableCollection<ResourceMapRowViewModel> Resources { get; } = [];
    public ObservableCollection<SettingFieldViewModel> Settings { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public IReadOnlyList<string> Roles { get; } = [SourceRoles.Mirror, SourceRoles.Primary];
    public IReadOnlyList<string> DeletePolicies { get; } = ["ask", "never", "always"];

    [ObservableProperty] private ConnectionStep step = ConnectionStep.Service;
    [ObservableProperty] private RegisteredSourceAdapter? selectedService;
    [ObservableProperty] private string baseUrlText = string.Empty;
    [ObservableProperty] private SourceAccount? selectedAccount;
    [ObservableProperty] private RemoteProjectSummary? selectedProject;
    [ObservableProperty] private string remoteProjectId = string.Empty;
    [ObservableProperty] private string connectionName = string.Empty;
    [ObservableProperty] private string role = SourceRoles.Mirror;
    [ObservableProperty] private string deletePolicy = "ask";
    [ObservableProperty] private bool pushStatuses = true;
    [ObservableProperty] private bool pushComments;
    [ObservableProperty] private bool connectOnOpen;
    [ObservableProperty] private string separator = ".";
    [ObservableProperty] private bool pullAfterSaving = true;
    [ObservableProperty] private string testResult = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = string.Empty;

    /// <summary>The saved connection once the wizard finished; null when cancelled.</summary>
    public SourceConnectionSettings? Saved { get; private set; }

    public bool RequiresAccount => SelectedService?.Descriptor.AuthMethods.Any(m => m is not NoSignInAuth) == true;
    public bool AllowsCustomAddress => SelectedService?.Descriptor.AllowsCustomBaseUrl == true;
    public bool CanDiscoverProjects => SelectedService?.Adapter is ISupportsProjectDiscovery;
    public bool HasProblems => Problems.Count > 0;
    public bool IsEditing { get; private set; }
    public string StepTitle => Step switch
    {
        ConnectionStep.Service => "Choose a service",
        ConnectionStep.Account => "Account",
        ConnectionStep.Project => "Remote project",
        ConnectionStep.Mapping => "Languages and resources",
        ConnectionStep.Policy => "Role and policy",
        _ => "Test the connection",
    };
    public bool IsService => Step == ConnectionStep.Service;
    public bool IsAccount => Step == ConnectionStep.Account;
    public bool IsProject => Step == ConnectionStep.Project;
    public bool IsMapping => Step == ConnectionStep.Mapping;
    public bool IsPolicy => Step == ConnectionStep.Policy;
    public bool IsTest => Step == ConnectionStep.Test;
    public bool CanGoBack => Step > ConnectionStep.Service && !IsEditing;
    public bool IsLastStep => Step == ConnectionStep.Test;

    public event EventHandler? CloseRequested;

    partial void OnStepChanged(ConnectionStep value)
    {
        foreach (var name in new[] { nameof(StepTitle), nameof(IsService), nameof(IsAccount), nameof(IsProject), nameof(IsMapping), nameof(IsPolicy), nameof(IsTest), nameof(CanGoBack), nameof(IsLastStep) })
            OnPropertyChanged(name);
    }

    partial void OnSelectedServiceChanged(RegisteredSourceAdapter? value)
    {
        OnPropertyChanged(nameof(RequiresAccount));
        OnPropertyChanged(nameof(AllowsCustomAddress));
        OnPropertyChanged(nameof(CanDiscoverProjects));
        BaseUrlText = value?.Descriptor.DefaultBaseUrl?.AbsoluteUri ?? string.Empty;
        Settings.Clear();
        foreach (var field in value?.Descriptor.ConnectionSettings?.Fields ?? []) Settings.Add(new(field, null));
        LoadAccounts();
    }

    /// <summary>Loads an existing connection to change its mapping and policy; the service and account stay as they are.</summary>
    public void Edit(SourceConnectionSettings existing)
    {
        IsEditing = true;
        _draft = existing.Clone();
        _added = true;
        SelectedService = Services.FirstOrDefault(s => s.Descriptor.Id == existing.Adapter);
        ConnectionName = existing.Name ?? string.Empty;
        RemoteProjectId = existing.RemoteProject?.Id ?? string.Empty;
        Role = existing.Role;
        DeletePolicy = existing.Policy.DeleteRemote;
        PushStatuses = existing.Policy.PushStatuses;
        PushComments = existing.Policy.PushComments;
        ConnectOnOpen = existing.Policy.ConnectOnOpen;
        Separator = existing.Keys.Separator;
        foreach (var field in Settings) if (existing.Settings.TryGetValue(field.Field.Key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String) field.Text = value.GetString() ?? string.Empty;
        Step = ConnectionStep.Mapping;
        _ = LoadMappingAsync(existing);
    }

    private void LoadAccounts()
    {
        Accounts.Clear();
        if (SelectedService is null) return;
        foreach (var account in _signIn.Accounts.Accounts.Where(a => a.Adapter == SelectedService.Descriptor.Id)) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault();
    }

    // ───────────── navigation ─────────────

    [RelayCommand]
    private async Task Next()
    {
        Problems.Clear();
        OnPropertyChanged(nameof(HasProblems));
        try
        {
            IsBusy = true;
            switch (Step)
            {
                case ConnectionStep.Service:
                    if (SelectedService is null) return;
                    Step = RequiresAccount ? ConnectionStep.Account : ConnectionStep.Project;
                    if (!RequiresAccount) await PrepareProjectStepAsync();
                    break;
                case ConnectionStep.Account:
                    if (SelectedAccount is null && !await AddAccountAsync()) return;
                    Step = ConnectionStep.Project;
                    await PrepareProjectStepAsync();
                    break;
                case ConnectionStep.Project:
                    if (RequiresAccount && string.IsNullOrWhiteSpace(RemoteProjectId) && SelectedProject is null)
                    {
                        Problems.Add("Choose or enter the remote project.");
                        break;
                    }
                    if (SelectedProject is not null) RemoteProjectId = SelectedProject.Id;
                    await EnsureConnectionAsync();
                    await LoadMappingAsync(_draft);
                    Step = ConnectionStep.Mapping;
                    break;
                case ConnectionStep.Mapping:
                    ApplyMapping();
                    foreach (var p in SourceMapping.Validate(_draft, _projectLanguages, _remote)) Problems.Add(p);
                    foreach (var c in Collisions()) Problems.Add(c);
                    if (Problems.Count == 0) Step = ConnectionStep.Policy;
                    break;
                case ConnectionStep.Policy:
                    ApplyPolicy();
                    foreach (var p in SourceMapping.Validate(_draft, _projectLanguages, _remote)) Problems.Add(p);
                    if (Problems.Count == 0) { Step = ConnectionStep.Test; await RunTestAsync(); }
                    break;
            }
        }
        catch (SourceException ex)
        {
            Problems.Add(ex.Error.Message);
        }
        catch (InvalidOperationException ex)
        {
            Problems.Add(ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasProblems));
        }
    }

    [RelayCommand]
    private void Back()
    {
        Problems.Clear();
        OnPropertyChanged(nameof(HasProblems));
        Step = Step switch
        {
            ConnectionStep.Account => ConnectionStep.Service,
            ConnectionStep.Project => RequiresAccount ? ConnectionStep.Account : ConnectionStep.Service,
            ConnectionStep.Mapping => ConnectionStep.Project,
            ConnectionStep.Policy => ConnectionStep.Mapping,
            ConnectionStep.Test => ConnectionStep.Policy,
            _ => Step,
        };
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (_added && !IsEditing && !_projectConnections.Contains(_draft)) await _connections.RemoveAsync(_draft.Id);
        Saved = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task Finish()
    {
        ApplyPolicy();
        _draft.Name = string.IsNullOrWhiteSpace(ConnectionName) ? _remote?.Name ?? _draft.Id : ConnectionName.Trim();
        if (IsEditing)
        {
            var live = _projectConnections.FirstOrDefault(c => c.Id == _draft.Id);
            if (live is not null) await _connections.ReplaceAsync(live.Id, _draft);
        }
        else if (!_projectConnections.Contains(_draft))
        {
            await _connections.RemoveAsync(_draft.Id); // re-add as the final settings so the project list holds exactly one entry
            await _connections.AddAsync(_draft);
        }
        Saved = _draft;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // ───────────── steps ─────────────

    [RelayCommand]
    private async Task SignIn() => await AddAccountAsync();

    private async Task<bool> AddAccountAsync()
    {
        if (SelectedService is null) return false;
        var methods = SelectedService.Descriptor.AuthMethods.Where(m => m is not NoSignInAuth).ToList();
        var method = methods.Count == 1 ? methods[0] : null;
        if (method is null)
        {
            var label = await _dialogs.ShowPickAsync("Sign in", "How do you want to sign in?", [.. methods.Select(m => m.Label)]);
            method = methods.FirstOrDefault(m => m.Label == label);
        }
        if (method is null) return false;
        var address = AllowsCustomAddress && Uri.TryCreate(BaseUrlText, UriKind.Absolute, out var parsed) ? parsed : null;
        var account = await _flow.RunAsync(SelectedService, address, method);
        if (account is null) return false;
        LoadAccounts();
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == account.Id) ?? account;
        _connections.Refresh();
        return true;
    }

    private async Task PrepareProjectStepAsync()
    {
        Projects.Clear();
        if (!CanDiscoverProjects || SelectedAccount is null) return;
        StatusText = "Loading projects…";
        try
        {
            await foreach (var project in _signIn.ListProjectsAsync(SelectedAccount)) Projects.Add(project);
            SelectedProject = Projects.FirstOrDefault();
        }
        catch (SourceException ex) { Problems.Add(ex.Error.Message); }
        finally { StatusText = string.Empty; }
    }

    /// <summary>Adds the connection with provisional settings, so the service can be asked about its languages and resources.</summary>
    private async Task EnsureConnectionAsync()
    {
        if (_added) return;
        var service = SelectedService!;
        var id = NewId(service.Descriptor.Id);
        _draft = new SourceConnectionSettings
        {
            Id = id,
            Adapter = service.Descriptor.Id,
            BaseUrl = AllowsCustomAddress && Uri.TryCreate(BaseUrlText, UriKind.Absolute, out var url) ? url : null,
            RemoteProject = new SourceRemoteProject { Id = string.IsNullOrWhiteSpace(RemoteProjectId) ? id : RemoteProjectId.Trim(), Name = SelectedProject?.Name },
        };
        ApplySettingFields();
        if (RequiresAccount && SelectedAccount is not null) _signIn.Accounts.Bind(_connections.WorkspaceId ?? string.Empty, id, SelectedAccount.Id);
        await _connections.AddAsync(_draft);
        _added = true;
    }

    private async Task LoadMappingAsync(SourceConnectionSettings connection)
    {
        Languages.Clear();
        Resources.Clear();
        try
        {
            var session = await _connections.GetSessionAsync(connection.Id);
            _remote = await session.DescribeAsync(CancellationToken.None);
        }
        catch (SourceException ex)
        {
            Problems.Add(ex.Error.Message);
            OnPropertyChanged(nameof(HasProblems));
            return;
        }
        var suggestions = SourceMapping.SuggestLanguages(_projectLanguages, _remote.Languages);
        foreach (var s in suggestions)
            Languages.Add(new(s.ProjectLanguage, connection.Languages.GetValueOrDefault(s.ProjectLanguage) ?? s.RemoteLanguage, s.IsExact || connection.Languages.ContainsKey(s.ProjectLanguage)));
        foreach (var r in _remote.Resources)
        {
            var mapped = connection.Resources.FirstOrDefault(m => m.Remote == r.Id);
            Resources.Add(new(r.Id, r.Name) { Include = connection.Resources.Count == 0 || mapped is not null, Package = mapped?.Package });
        }
        if (string.IsNullOrWhiteSpace(ConnectionName)) ConnectionName = _remote.Name;
    }

    private void ApplyMapping()
    {
        _draft.Languages = Languages.Where(l => !string.IsNullOrWhiteSpace(l.Remote)).ToDictionary(l => l.ProjectLanguage, l => l.Remote!, StringComparer.Ordinal);
        _draft.Resources = [.. Resources.Where(r => r.Include).Select(r => new SourceResourceMap { Remote = r.RemoteId, Package = string.IsNullOrWhiteSpace(r.Package) ? null : r.Package })];
        if (_draft.Resources.Count == Resources.Count && Resources.All(r => string.IsNullOrWhiteSpace(r.Package))) _draft.Resources = [];
        _draft.Keys.Separator = string.IsNullOrEmpty(Separator) ? "." : Separator;
        ApplySettingFields();
    }

    private void ApplyPolicy()
    {
        _draft.Role = Role;
        _draft.Policy = new SourceSyncPolicy { ConnectOnOpen = ConnectOnOpen, PushStatuses = PushStatuses, PushComments = PushComments, DeleteRemote = DeletePolicy };
        _draft.Name = string.IsNullOrWhiteSpace(ConnectionName) ? _draft.Name : ConnectionName.Trim();
    }

    private void ApplySettingFields()
    {
        foreach (var field in Settings)
            _draft.Settings[field.Field.Key] = System.Text.Json.JsonSerializer.SerializeToElement(field.Text);
    }

    private IEnumerable<string> Collisions()
    {
        var mapper = new SourceKeyMapper(_draft.Keys);
        foreach (var group in mapper.Collisions(_projectKeyPaths()))
            yield return $"{group.Count} keys would become the same remote key: {string.Join(", ", group.Take(3))}.";
    }

    /// <summary>Key paths of the open project, supplied by the host so the wizard can refuse a mapping that makes two keys collide.</summary>
    public Func<IEnumerable<IReadOnlyList<string>>> ProjectKeyPaths { get; set; } = () => [];
    private IEnumerable<IReadOnlyList<string>> _projectKeyPaths() => ProjectKeyPaths();

    private async Task RunTestAsync()
    {
        TestResult = "Testing…";
        var result = await _connections.TestAsync(_draft.Id);
        TestResult = result.Succeeded ? result.Message : "Could not connect: " + result.Message;
    }

    [RelayCommand]
    private async Task RetryTest() => await RunTestAsync();

    [RelayCommand]
    private async Task ChooseFolder(SettingFieldViewModel? field)
    {
        if (field is null) return;
        var folder = await _dialogs.SelectFolderAsync(field.Text, field.Label);
        if (!string.IsNullOrEmpty(folder)) field.Text = folder;
    }

    private string NewId(string adapterId)
    {
        var stem = adapterId.Split('.')[^1];
        var id = stem;
        for (var i = 2; _projectConnections.Any(c => c.Id == id); i++) id = $"{stem}-{i}";
        return id;
    }
}
