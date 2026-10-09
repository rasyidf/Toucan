namespace Toucan.Plugins;

/// <summary>
/// What the host offers a plugin beyond registering things: somewhere to keep settings and files, secrets, notifications,
/// background work and redacted diagnostics. One instance per plugin, so everything is scoped to the plugin's ID.
/// Members work once the application has started; during <see cref="IToucanPlugin.Initialize"/> only registration is allowed.
/// </summary>
/// <remarks>
/// Plugins run inside Toucan's process with the user's permissions and are not sandboxed. These services keep a
/// well-behaved plugin tidy and keep its secrets out of logs; they do not stop a malicious one, which is why the user
/// has to trust each plugin first.
/// </remarks>
public interface IPluginServices
{
    IPluginStorage Storage { get; }
    IPluginConfiguration Configuration { get; }
    IPluginSecrets Secrets { get; }
    IPluginNotifier Notifier { get; }
    IBackgroundOperations Operations { get; }
    IPluginDiagnostics Diagnostics { get; }
}

// ───────────────────────────── storage ─────────────────────────────

/// <summary>
/// A folder the plugin may write to. It is outside the plugin's own folder: that folder is trusted by its hash, and writing
/// there would make Toucan ask for trust again.
/// </summary>
public interface IPluginStorage
{
    /// <summary>The folder (created on first use). Paths below are relative to it.</summary>
    string DataDirectory { get; }

    /// <summary>Full path for a relative one. Throws <see cref="ArgumentException"/> when it would leave <see cref="DataDirectory"/>.</summary>
    string ResolvePath(string relativePath);

    bool Exists(string relativePath);

    Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Writes through a temporary file and a rename, so a crash never leaves half a file.</summary>
    Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken = default);

    Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default);

    Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default);

    bool Delete(string relativePath);
}

// ─────────────────────────── configuration ───────────────────────────

/// <summary>How far a setting reaches.</summary>
public enum ConfigScope
{
    /// <summary>One value for the whole application.</summary>
    App,

    /// <summary>One value per open project (folder).</summary>
    Workspace,

    /// <summary>One value per connection, optionally within a project.</summary>
    Connection,
}

/// <summary>Which value of a setting: the app's, a project's or a connection's.</summary>
public readonly record struct ConfigTarget(ConfigScope Scope, string? WorkspaceId = null, string? ConnectionId = null)
{
    public static ConfigTarget App => new(ConfigScope.App);

    /// <param name="workspaceId">The project's ID, as in <c>IPluginWorkspace.WorkspaceId</c> (its folder).</param>
    public static ConfigTarget ForWorkspace(string workspaceId) => new(ConfigScope.Workspace, workspaceId);

    /// <param name="connectionId">The plugin's own ID for the connection.</param>
    /// <param name="workspaceId">The project the connection belongs to, or null for a connection shared by every project.</param>
    public static ConfigTarget ForConnection(string connectionId, string? workspaceId = null) => new(ConfigScope.Connection, workspaceId, connectionId);
}

public enum ConfigFieldType
{
    Text,
    Boolean,
    WholeNumber,
    Number,

    /// <summary>One of <see cref="ConfigField.Choices"/>.</summary>
    Choice,

    /// <summary>Kept in the secret store, never in the settings file or the logs. <c>GetValue</c> never returns it; use <c>GetSecretAsync</c>.</summary>
    Secret,

    /// <summary>A file or folder path.</summary>
    Path,

    /// <summary>An absolute http or https URL.</summary>
    Url,
}

public sealed record ConfigChoice(string Value, string Label);

/// <summary>One typed setting: what it is, its default, and what counts as a valid value.</summary>
public sealed record ConfigField
{
    /// <summary>Stable key: letters, digits, '.', '_' and '-'. Renaming one needs a migration.</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>Labels by culture (<c>fr</c>, <c>pt-BR</c>); the host falls back to the parent culture, then <see cref="Label"/>.</summary>
    public IReadOnlyDictionary<string, string> LocalizedLabels { get; init; } = new Dictionary<string, string>();

    public string? Description { get; init; }

    public ConfigFieldType Type { get; init; } = ConfigFieldType.Text;

    public ConfigScope Scope { get; init; } = ConfigScope.App;

    /// <summary>Value when nothing is stored. Must suit <see cref="Type"/> (a string, bool, long, double or the choice's value).</summary>
    public object? Default { get; init; }

    /// <summary>A value must be present: text not empty, a secret stored.</summary>
    public bool Required { get; init; }

    /// <summary>Lowest allowed number, or shortest allowed text.</summary>
    public double? Minimum { get; init; }

    /// <summary>Highest allowed number, or longest allowed text.</summary>
    public double? Maximum { get; init; }

    /// <summary>Regular expression a text value must match.</summary>
    public string? Pattern { get; init; }

    public IReadOnlyList<ConfigChoice> Choices { get; init; } = [];
}

/// <summary>Brings stored values written for <see cref="FromVersion"/> up to the next schema version.</summary>
/// <param name="FromVersion">The schema version the stored values have.</param>
/// <param name="Apply">Edits one scope's values in place: rename keys, convert types, drop what is gone.</param>
public sealed record ConfigMigration(int FromVersion, Action<ConfigScope, Dictionary<string, object?>> Apply);

/// <summary>The settings a plugin has, given to the host with <see cref="IPluginContext.SetConfiguration"/>.</summary>
public sealed record ConfigSchema
{
    /// <summary>Raise it whenever a change would misread old stored values, and add a <see cref="ConfigMigration"/> from the old number.</summary>
    public int Version { get; init; } = 1;

    public IReadOnlyList<ConfigField> Fields { get; init; } = [];

    public IReadOnlyList<ConfigMigration> Migrations { get; init; } = [];
}

public readonly record struct ConfigValidationResult(bool IsValid, string? Error = null)
{
    public static ConfigValidationResult Ok => new(true);
    public static ConfigValidationResult Fail(string error) => new(false, error);
}

public sealed class ConfigChangedEventArgs(string key, ConfigTarget target) : EventArgs
{
    public string Key { get; } = key;
    public ConfigTarget Target { get; } = target;
}

/// <summary>Typed, validated settings with defaults, scopes and migrations, stored outside the plugin folder.</summary>
public interface IPluginConfiguration
{
    /// <summary>The schema the plugin gave the host, or null when it has none.</summary>
    ConfigSchema? Schema { get; }

    /// <summary>The stored value, or the field's default. Secrets are never returned; <typeparamref name="T"/> must suit the field type.</summary>
    T? GetValue<T>(string key, ConfigTarget? target = null);

    /// <summary>Checks a value against the field's rules without storing it.</summary>
    ConfigValidationResult Validate(string key, object? value);

    /// <summary>Validates and stores a value; an invalid one is refused and nothing changes. A secret goes to the secret store. <c>null</c> restores the default.</summary>
    Task<ConfigValidationResult> SetAsync(string key, object? value, ConfigTarget? target = null, CancellationToken cancellationToken = default);

    /// <summary>The secret behind a <see cref="ConfigFieldType.Secret"/> field, or null.</summary>
    Task<string?> GetSecretAsync(string key, ConfigTarget? target = null, CancellationToken cancellationToken = default);

    event EventHandler<ConfigChangedEventArgs>? Changed;
}

// ───────────────────────────── secrets ─────────────────────────────

/// <summary>
/// Credentials for the plugin, kept encrypted by the host and scoped to the plugin and, if given, a project or connection.
/// Values read or written here are masked in diagnostics and logs.
/// </summary>
public interface IPluginSecrets
{
    Task<string?> GetAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="value"/>; null or empty removes it.</summary>
    Task SetAsync(string name, string? value, ConfigTarget? target = null, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default);
}

// ──────────────────────────── notifications ────────────────────────────

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record PluginNotification
{
    public required string Title { get; init; }
    public string? Message { get; init; }
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;

    /// <summary>A command (usually the plugin's own) the notification offers as a button, with <see cref="ActionLabel"/>.</summary>
    public string? ActionCommandId { get; init; }
    public string? ActionLabel { get; init; }

    /// <summary>Stays on screen until dismissed, for something the user has to act on.</summary>
    public bool Sticky { get; init; }
}

public interface IPluginNotifier
{
    /// <summary>Shows a notification and keeps it in the history. Never blocks. Without a UI (the CLI) it goes to the console.</summary>
    void Notify(PluginNotification notification);
}

// ────────────────────────── background operations ──────────────────────────

public enum OperationStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record OperationOptions
{
    /// <summary>What the user sees while it runs, e.g. "Pulling translations".</summary>
    public required string Title { get; init; }

    /// <summary>Cancel when the project closes, so work for one project never keeps going into the next.</summary>
    public bool CancelWithWorkspace { get; init; } = true;

    /// <summary>Whether the user may cancel it from the status bar.</summary>
    public bool IsCancellable { get; init; } = true;
}

/// <summary>Passed to the work of a background operation.</summary>
public interface IOperationContext
{
    /// <summary>Tells the user how far along it is. Safe to call from any thread.</summary>
    void Report(string? message, double? fraction = null);
}

public interface IOperationHandle
{
    Guid Id { get; }
    string Title { get; }
    OperationStatus Status { get; }

    /// <summary>What went wrong, once <see cref="Status"/> is <see cref="OperationStatus.Failed"/>. Secrets are masked.</summary>
    string? FailureMessage { get; }

    /// <summary>Completes when the work ends, however it ends; it never throws.</summary>
    Task Completion { get; }

    void Cancel();
}

public interface IBackgroundOperations
{
    /// <summary>
    /// Runs <paramref name="work"/> in the background, shown with progress and a cancel button. An exception ends it as
    /// failed and is reported; it never reaches the caller or the application.
    /// </summary>
    IOperationHandle Start(OperationOptions options, Func<IOperationContext, CancellationToken, Task> work);

    IReadOnlyList<IOperationHandle> Active { get; }
}

// ───────────────────────────── diagnostics ─────────────────────────────

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error,
}

public interface IPluginDiagnostics
{
    /// <summary>The text with secrets masked: values passed through <see cref="IPluginSecrets"/>, bearer tokens, <c>key=value</c> credentials, URL passwords and long token-like strings.</summary>
    string Redact(string text);

    /// <summary>Adds a line to the plugin's part of the diagnostics report (what "Copy diagnostics" collects). It is redacted first.</summary>
    void Write(DiagnosticLevel level, string message);

    /// <summary>Marks a value that is secret but did not come from <see cref="IPluginSecrets"/> (a token received from a server), so it is masked from now on.</summary>
    void RegisterSecret(string value);
}
