using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Core.Contracts;
using Toucan.Core.Services;

namespace Toucan.Core.Sources;

/// <summary>
/// Who the user is on one service instance: the adapter, the address and how they signed in. Stored per user, never in
/// a project; the credential itself is in the secret store under <see cref="CredentialKey"/>.
/// </summary>
public sealed record SourceAccount
{
    public required string Id { get; init; }
    public required string Adapter { get; init; }
    public required Uri BaseUrl { get; init; }

    /// <summary>The adapter's sign-in method ID used for this account.</summary>
    public required string AuthMethod { get; init; }
    public required string DisplayName { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    [JsonIgnore] public string CredentialKey => SourceAccounts.CredentialKey(Id);
}

/// <summary>How a connection found its account.</summary>
public enum AccountResolution
{
    /// <summary>The account the user chose for this connection earlier, or the only one that fits.</summary>
    Resolved,

    /// <summary>No account for this adapter and address: the user signs in first.</summary>
    None,

    /// <summary>Several fit and none was chosen for this connection: the user picks one.</summary>
    Ambiguous,
}

public sealed record AccountMatch(AccountResolution Resolution, SourceAccount? Account, IReadOnlyList<SourceAccount> Candidates);

public interface ISourceAccountStore
{
    IReadOnlyList<SourceAccount> Accounts { get; }
    SourceAccount? Find(string accountId);
    SourceAccount Add(string adapter, Uri baseUrl, string authMethod, string displayName);
    void Update(SourceAccount account);

    /// <summary>Removes the account and the connections' choices of it. The caller removes its credential.</summary>
    bool Remove(string accountId);

    /// <summary>The account a connection uses: its remembered choice, else the only matching account.</summary>
    AccountMatch Resolve(string adapter, Uri baseUrl, string workspaceId, string connectionId);

    /// <summary>Remembers which account a project's connection uses (only on this machine, for this user).</summary>
    void Bind(string workspaceId, string connectionId, string accountId);

    /// <summary>Connections that chose this account, as the hashed project ID (see <c>SecretKeys.ProjectId</c>) and the connection ID.</summary>
    IReadOnlyList<(string ProjectId, string ConnectionId)> UsageOf(string accountId) => [];
}

public static class SourceAccounts
{
    /// <summary>The secret-store key of an account's credential.</summary>
    public static string CredentialKey(string accountId) => $"sources/{accountId}/credential";

    /// <summary>Two addresses point at the same service instance when scheme, host and port match.</summary>
    public static bool SameInstance(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}

/// <summary>
/// <c>accounts.json</c> in the user's Toucan folder, next to the secret store. Re-read when another process (the CLI, a
/// second window) changes it; written atomically with owner-only permissions. Thread-safe.
/// </summary>
public sealed class SourceAccountStore : ISourceAccountStore
{
    private sealed class AccountFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("accounts")] public List<SourceAccount> Accounts { get; set; } = [];

        /// <summary><c>{project id}/{connection id}</c> → account ID.</summary>
        [JsonPropertyName("bindings")] public SortedDictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Lock _gate = new();
    private readonly string _file;
    private AccountFile _data = new();
    private DateTime _stamp = DateTime.MinValue;

    public SourceAccountStore() : this(Path.Combine(SecureStorageService.DefaultFolder, "accounts.json")) { }

    public SourceAccountStore(string file) => _file = file;

    public string FilePath => _file;

    public IReadOnlyList<SourceAccount> Accounts
    {
        get { lock (_gate) { Refresh(); return [.. _data.Accounts]; } }
    }

    public SourceAccount? Find(string accountId)
    {
        lock (_gate) { Refresh(); return _data.Accounts.Find(a => a.Id == accountId); }
    }

    public SourceAccount Add(string adapter, Uri baseUrl, string authMethod, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(authMethod);
        lock (_gate)
        {
            Refresh();
            var account = new SourceAccount
            {
                Id = "acc_" + Guid.NewGuid().ToString("N")[..10],
                Adapter = adapter,
                BaseUrl = baseUrl,
                AuthMethod = authMethod,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? baseUrl.Host : displayName,
            };
            _data.Accounts.Add(account);
            Write();
            return account;
        }
    }

    public void Update(SourceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (_gate)
        {
            Refresh();
            var index = _data.Accounts.FindIndex(a => a.Id == account.Id);
            if (index < 0) throw new InvalidOperationException($"No account {account.Id}.");
            _data.Accounts[index] = account;
            Write();
        }
    }

    public bool Remove(string accountId)
    {
        lock (_gate)
        {
            Refresh();
            if (_data.Accounts.RemoveAll(a => a.Id == accountId) == 0) return false;
            foreach (var key in _data.Bindings.Where(b => b.Value == accountId).Select(b => b.Key).ToList()) _data.Bindings.Remove(key);
            Write();
            return true;
        }
    }

    public AccountMatch Resolve(string adapter, Uri baseUrl, string workspaceId, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        lock (_gate)
        {
            Refresh();
            var candidates = _data.Accounts.Where(a => string.Equals(a.Adapter, adapter, StringComparison.OrdinalIgnoreCase)
                                                       && a.BaseUrl is not null && SourceAccounts.SameInstance(a.BaseUrl, baseUrl)).ToList();
            if (_data.Bindings.TryGetValue(BindingKey(workspaceId, connectionId), out var bound) && candidates.Find(a => a.Id == bound) is { } chosen)
                return new AccountMatch(AccountResolution.Resolved, chosen, candidates);
            return candidates.Count switch
            {
                0 => new AccountMatch(AccountResolution.None, null, candidates),
                1 => new AccountMatch(AccountResolution.Resolved, candidates[0], candidates),
                _ => new AccountMatch(AccountResolution.Ambiguous, null, candidates),
            };
        }
    }

    public void Bind(string workspaceId, string connectionId, string accountId)
    {
        lock (_gate)
        {
            Refresh();
            if (!_data.Accounts.Exists(a => a.Id == accountId)) throw new InvalidOperationException($"No account {accountId}.");
            _data.Bindings[BindingKey(workspaceId, connectionId)] = accountId;
            Write();
        }
    }

    public IReadOnlyList<(string ProjectId, string ConnectionId)> UsageOf(string accountId)
    {
        lock (_gate)
        {
            Refresh();
            return [.. _data.Bindings.Where(b => b.Value == accountId).Select(b =>
            {
                var cut = b.Key.LastIndexOf('/');
                return cut < 0 ? (b.Key, string.Empty) : (b.Key[..cut], b.Key[(cut + 1)..]);
            })];
        }
    }

    // The project's folder path is not stored, only a hash of it, like project-scoped secrets.
    private static string BindingKey(string workspaceId, string connectionId) => $"{SecretKeys.ProjectId(workspaceId)}/{connectionId}";

    private void Refresh()
    {
        var stamp = File.Exists(_file) ? File.GetLastWriteTimeUtc(_file) : DateTime.MinValue;
        if (stamp == _stamp) return;
        _stamp = stamp;
        _data = new AccountFile();
        if (stamp == DateTime.MinValue) return;
        try
        {
            var read = JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(_file), s_json);
            if (read is { Version: 1 })
            {
                _data = read;
                _data.Accounts ??= [];
                _data.Bindings ??= new(StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable file behaves as empty and is replaced on the next change.
        }
    }

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, s_json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _file, overwrite: true);
        _stamp = File.GetLastWriteTimeUtc(_file);
    }
}
