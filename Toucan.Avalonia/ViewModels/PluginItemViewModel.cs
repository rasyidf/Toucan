using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One row of Settings → Plugins: what was found, its trust state, and the actions on it.</summary>
public partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginLoadResult _result;
    private readonly IPluginPolicyStore? _policy;
    private readonly IAsyncMessageService _messages;
    private readonly Action _changed;
    private bool _loadingEnabled = true;

    public PluginItemViewModel(PluginLoadResult result, IPluginPolicyStore? policy, IAsyncMessageService messages, Action changed)
    {
        _result = result;
        _policy = policy;
        _messages = messages;
        _changed = changed;

        isEnabled = result.Status != PluginStatus.Disabled;
        _loadingEnabled = false;
        trusted = result.Trust == PluginTrustState.Trusted || result.Status == PluginStatus.Loaded && result.Trust is null;
        RefreshStatus();
    }

    public string Id => _result.DisplayId;
    public string Name => _result.Manifest?.Name is { Length: > 0 } n ? n : Id;
    public string Version => _result.Manifest?.Version ?? string.Empty;
    public string Author => _result.Manifest?.Author ?? string.Empty;
    public string Description => _result.Manifest?.Description ?? string.Empty;
    public string Folder => _result.Directory;
    public string Hash => _result.ContentHash ?? string.Empty;
    public string ShortHash => Hash.Length >= 12 ? Hash[..12] + "…" : Hash;
    public string ProvidesText => _result.Registered is { Count: > 0 } r ? string.Join(", ", r) : string.Empty;
    public string SignatureText => _result.Signature switch
    {
        PluginSignatureStatus.Valid => "Signed",
        PluginSignatureStatus.Invalid => "Invalid signature",
        _ => "Not signed",
    };

    /// <summary>Only plugins whose files could be read can be trusted; rejected ones (bad manifest, invalid signature) cannot.</summary>
    public bool CanManage => _policy is not null && _result.Manifest is not null && _result.Status != PluginStatus.Rejected;

    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanTrust), nameof(CanRevoke))] private bool trusted;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsGood), nameof(IsWarn), nameof(IsBad))] private string statusText = string.Empty;
    [ObservableProperty] private string note = string.Empty;

    public bool IsGood => StatusText.StartsWith("Loaded", StringComparison.Ordinal) || StatusText.StartsWith("Trusted", StringComparison.Ordinal);
    public bool IsWarn => StatusText.StartsWith("Not trusted", StringComparison.Ordinal) || StatusText.StartsWith("Changed", StringComparison.Ordinal);
    public bool IsBad => StatusText.StartsWith("Failed", StringComparison.Ordinal) || StatusText.StartsWith("Rejected", StringComparison.Ordinal);
    public bool HasDescription => Description.Length > 0;
    public bool HasAuthor => Author.Length > 0;
    public bool HasProvides => ProvidesText.Length > 0;
    public bool CanTrust => CanManage && !Trusted && Hash.Length > 0;
    public bool CanRevoke => CanManage && Trusted && Hash.Length > 0;
    public bool HasNote => Note.Length > 0;

    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(HasNote));

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loadingEnabled || _policy is null || _result.Manifest is null) return;
        _policy.SetEnabled(_result.Manifest.Id, value);
        RefreshStatus(pending: true);
        _changed();
    }

    [RelayCommand]
    private async Task Trust()
    {
        if (_policy is null || _result.Manifest is null || Hash.Length == 0) return;

        var by = Author.Length > 0 ? $" by {Author}" : string.Empty;
        var message = $"Trust “{Name}” {Version}{by}?\n\nA plugin runs code with your permissions, like any program you install. " +
                      $"Toucan will load exactly these files (SHA-256 {ShortHash}) and will ask again if they change.\n\n" +
                      $"Signature: {SignatureText}.\nFolder: {Folder}";
        if (!await _messages.ConfirmAsync(message, "Trust plugin", "Trust", "Cancel")) return;

        _policy.Trust(_result.Manifest.Id, Hash);
        Trusted = true;
        RefreshStatus(pending: true);
        _changed();
    }

    [RelayCommand]
    private void Revoke()
    {
        if (_policy is null || _result.Manifest is null) return;
        _policy.Revoke(_result.Manifest.Id);
        Trusted = false;
        RefreshStatus(pending: true);
        _changed();
    }

    [RelayCommand]
    private void OpenFolder() => PlatformService.RevealInFileManager(Folder);

    private void RefreshStatus(bool pending = false)
    {
        if (pending)
        {
            StatusText = !IsEnabled ? "Disabled after restart"
                : Trusted ? "Trusted · loads after restart"
                : "Not trusted · won't load after restart";
            Note = string.Empty;
            return;
        }

        StatusText = _result.Status switch
        {
            PluginStatus.Loaded => "Loaded",
            PluginStatus.Disabled => "Disabled",
            PluginStatus.NeedsTrust => _result.Trust == PluginTrustState.Changed ? "Changed since trusted · not loaded" : "Not trusted · not loaded",
            PluginStatus.Rejected => "Rejected",
            _ => "Failed to load",
        };
        Note = _result.Status is PluginStatus.Rejected or PluginStatus.Failed ? _result.Error ?? string.Empty : string.Empty;
    }
}
