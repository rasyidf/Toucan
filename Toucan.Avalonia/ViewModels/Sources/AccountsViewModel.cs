using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;

namespace Toucan.Avalonia.ViewModels;

public sealed class AccountItemViewModel(SourceAccount account, string adapterName, IReadOnlyList<string> projects)
{
    public SourceAccount Account { get; } = account;
    public string Name => Account.DisplayName;
    public string Service => $"{adapterName} · {Account.BaseUrl.Host}";
    public string UsageText => projects.Count == 0 ? "Not used by any project" : "Used by " + string.Join(", ", projects);
    public string AccessibleName => $"{Name}, {Service}, {UsageText}";
}

/// <summary>The accounts page: who the user is on each service, which projects use each, and signing in and out.</summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly SourceSignInService _signIn;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly IRecentProjectService _recents;
    private readonly IDialogService _dialogs;
    private readonly IAsyncMessageService _messages;
    private readonly ISourceConnectionService? _connections;
    private readonly SourceSignInFlow _flow;

    public AccountsViewModel(SourceSignInService signIn, ISourceAdapterCatalog adapters, IRecentProjectService recents, IDialogService dialogs,
        IAsyncMessageService messages, ISourceConnectionService? connections)
    {
        (_signIn, _adapters, _recents, _dialogs, _messages, _connections) = (signIn, adapters, recents, dialogs, messages, connections);
        _flow = new SourceSignInFlow(signIn, dialogs, messages);
        Reload();
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];
    public IReadOnlyList<RegisteredSourceAdapter> Services => [.. _adapters.Adapters.Where(a => a.Descriptor.AuthMethods.Any(m => m is not Toucan.Plugins.Sources.NoSignInAuth))];

    [ObservableProperty] private AccountItemViewModel? selected;
    [ObservableProperty] private RegisteredSourceAdapter? newAccountService;

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasNoAccounts => Accounts.Count == 0;

    public void Reload()
    {
        var names = _recents.LoadRecent().ToDictionary(p => SecretKeys.ProjectId(p.Path), p => System.IO.Path.GetFileName(p.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : p.Path);
        Accounts.Clear();
        foreach (var account in _signIn.Accounts.Accounts.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var projects = _signIn.Accounts.UsageOf(account.Id).Select(u => names.GetValueOrDefault(u.ProjectId, "another project")).Distinct().Order().ToList();
            Accounts.Add(new(account, _adapters.Find(account.Adapter)?.Descriptor.DisplayName ?? account.Adapter, projects));
        }
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoAccounts));
        Selected = Accounts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Add()
    {
        var adapter = NewAccountService ?? Services.FirstOrDefault();
        if (adapter is null) { await _messages.ShowMessageAsync("No installed service needs an account.", "Accounts"); return; }
        var methods = adapter.Descriptor.AuthMethods.Where(m => m is not Toucan.Plugins.Sources.NoSignInAuth).ToList();
        var method = methods.Count == 1 ? methods[0] : await ChooseAsync(methods);
        if (method is null) return;
        Uri? address = null;
        if (adapter.Descriptor.AllowsCustomBaseUrl)
        {
            var text = await _dialogs.ShowPromptAsync("Server address", $"Address of your {adapter.Descriptor.DisplayName} server (https://…).", adapter.Descriptor.DefaultBaseUrl?.AbsoluteUri ?? "https://");
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out address)) { await _messages.ShowMessageAsync("That is not a valid address.", "Accounts"); return; }
        }
        if (await _flow.RunAsync(adapter, address, method) is not null) { Reload(); _connections?.Refresh(); }
    }

    [RelayCommand]
    private async Task SignInAgain(AccountItemViewModel? item)
    {
        item ??= Selected;
        if (item is null || _adapters.Find(item.Account.Adapter) is not { } adapter) return;
        var method = adapter.Descriptor.AuthMethods.FirstOrDefault(m => m.Id == item.Account.AuthMethod);
        if (method is null) return;
        if (await _flow.RunAsync(adapter, item.Account.BaseUrl, method, item.Account) is not null) { Reload(); _connections?.Refresh(); }
    }

    [RelayCommand]
    private async Task SignOut(AccountItemViewModel? item)
    {
        item ??= Selected;
        if (item is null) return;
        var projects = item.UsageText;
        if (!await _messages.ConfirmAsync($"Sign out of {item.Name}? {projects}. The stored sign-in is revoked where the service allows it; projects keep their connections and ask you to sign in again.", "Sign out", "Sign out", "Stay signed in")) return;
        await _signIn.SignOutAsync(item.Account.Id);
        Reload();
        _connections?.Refresh();
    }

    private async Task<Toucan.Plugins.Sources.SourceAuthMethod?> ChooseAsync(IReadOnlyList<Toucan.Plugins.Sources.SourceAuthMethod> methods)
    {
        var label = await _dialogs.ShowPickAsync("Sign in", "How do you want to sign in?", [.. methods.Select(m => m.Label)]);
        return methods.FirstOrDefault(m => m.Label == label);
    }
}
