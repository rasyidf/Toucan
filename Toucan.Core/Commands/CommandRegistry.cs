using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Core.Commands;

/// <summary>A command known to the host. <see cref="PluginId"/> is null for built-in commands.</summary>
public sealed record RegisteredCommand(CommandDefinition Definition, ICommandHandler Handler, string? PluginId)
{
    public string Id => Definition.Id;
}

public enum CommandRunStatus
{
    Running,
    Completed,
    Cancelled,
    Failed,

    /// <summary>The command did not start because its state did not allow it.</summary>
    Refused,
}

/// <summary>Snapshot of one execution, raised as it starts, reports progress and ends.</summary>
public sealed record CommandRun(Guid RunId, string CommandId, CommandRunStatus Status, CommandProgressInfo? Progress = null, string? Error = null, CommandState? RefusedBecause = null);

public sealed class CommandRunEventArgs(CommandRun run) : EventArgs
{
    public CommandRun Run { get; } = run;
}

public sealed class ShortcutChangedEventArgs(string? commandId) : EventArgs
{
    /// <summary>The command whose shortcut changed, or null when many did (overrides loaded).</summary>
    public string? CommandId { get; } = commandId;
}

public sealed record ShortcutConflict(string CommandId, string Shortcut);

/// <summary>
/// Every command the application offers, built-in or from a plugin, with its state, execution and shortcut.
/// Menus, the palette and key handling read from here instead of keeping their own tables.
/// </summary>
public interface ICommandRegistry : ICommandContext
{
    IReadOnlyList<RegisteredCommand> Commands { get; }

    event EventHandler<CommandRunEventArgs>? RunChanged;
    event EventHandler<ShortcutChangedEventArgs>? ShortcutsChanged;
    event EventHandler? CommandsChanged;

    /// <summary>Adds a command. Throws <see cref="PluginRegistrationException"/> on a malformed or duplicate ID.</summary>
    void Register(CommandDefinition definition, ICommandHandler handler, string? pluginId = null);

    /// <summary>Removes a command (used when a built-in is rebound to a new window). Its shortcut override is kept.</summary>
    bool Unregister(string id);

    RegisteredCommand? Find(string id);

    CommandState GetState(string id);

    /// <summary>Runs a command. Never throws for handler errors: they end the run as <see cref="CommandRunStatus.Failed"/>.</summary>
    Task<CommandRun> ExecuteAsync(string id, object? parameter = null, CancellationToken cancellationToken = default);

    /// <summary>Executions that have not ended.</summary>
    IReadOnlyList<CommandRun> ActiveRuns { get; }

    void CancelRuns(string id);

    /// <summary>Normalized shortcut in effect (user override, else default), or null when unbound.</summary>
    string? GetShortcut(string id);

    IReadOnlyDictionary<string, string> CustomShortcuts { get; }

    /// <summary>
    /// CustomShortcuts a shortcut. <c>null</c> restores the default; an empty string unbinds. Returns false with the
    /// conflicting commands when another command already uses the shortcut, unless <paramref name="replaceConflicts"/>
    /// (which unbinds them).
    /// </summary>
    bool TrySetShortcut(string id, string? shortcut, out IReadOnlyList<ShortcutConflict> conflicts, bool replaceConflicts = false);

    IReadOnlyList<ShortcutConflict> FindConflicts(string shortcut, string? exceptCommandId = null);

    /// <summary>Replaces all overrides (from saved settings), skipping unknown IDs and malformed shortcuts.</summary>
    void LoadCustomShortcuts(IReadOnlyDictionary<string, string>? saved);

    /// <summary>Title in <paramref name="culture"/>, falling back to the parent culture and then the default title.</summary>
    string GetTitle(string id, CultureInfo? culture = null);

    string GetCategory(string id, CultureInfo? culture = null);
}

public sealed partial class CommandRegistry : ICommandRegistry, IDisposable
{
    private readonly object _gate = new();
    private readonly List<RegisteredCommand> _commands = [];
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, ActiveRun> _runs = new();
    private readonly HashSet<string> _openWorkspaces = new(StringComparer.Ordinal);
    private readonly IPluginActivationService? _activation;
    private readonly ILogger _logger;
    private readonly bool _isMac;
    private string? _workspaceId;

    public CommandRegistry(IPluginActivationService? activation = null, IEnumerable<PluginCommandRegistration>? pluginCommands = null,
        ILogger<CommandRegistry>? logger = null, bool? isMac = null)
    {
        _activation = activation;
        _logger = logger ?? NullLogger<CommandRegistry>.Instance;
        _isMac = isMac ?? OperatingSystem.IsMacOS();
        if (_activation is not null)
        {
            _activation.WorkspaceOpened += OnWorkspaceOpened;
            _activation.WorkspaceClosed += OnWorkspaceClosed;
        }

        foreach (var registration in pluginCommands ?? [])
        {
            try
            {
                Register(registration.Definition, registration.Handler, registration.PluginId);
            }
            catch (PluginRegistrationException ex)
            {
                _logger.LogWarning("Plugin {Id} command {Command} was not registered: {Error}", registration.PluginId, registration.Definition.Id, ex.Message);
            }
        }
    }

    public event EventHandler<CommandRunEventArgs>? RunChanged;
    public event EventHandler<ShortcutChangedEventArgs>? ShortcutsChanged;
    public event EventHandler? CommandsChanged;

    public bool HasWorkspace { get { lock (_gate) return _openWorkspaces.Count > 0; } }
    public string? WorkspaceId { get { lock (_gate) return _workspaceId; } }

    public IReadOnlyList<RegisteredCommand> Commands { get { lock (_gate) return _commands.ToList(); } }
    public IReadOnlyDictionary<string, string> CustomShortcuts { get { lock (_gate) return new Dictionary<string, string>(_overrides); } }
    public IReadOnlyList<CommandRun> ActiveRuns => _runs.Values.Select(r => r.Latest).ToList();

    public void Dispose()
    {
        if (_activation is null) return;
        _activation.WorkspaceOpened -= OnWorkspaceOpened;
        _activation.WorkspaceClosed -= OnWorkspaceClosed;
    }

    public void Register(CommandDefinition definition, ICommandHandler handler, string? pluginId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);

        if (string.IsNullOrEmpty(definition.Id) || !IdPattern().IsMatch(definition.Id))
            throw new PluginRegistrationException($"Command ID '{definition.Id}' is invalid: use lowercase letters, digits, '.' and '-'.");
        if (pluginId is not null && (pluginId == "toucan" || pluginId.StartsWith("toucan.", StringComparison.Ordinal)))
            throw new PluginRegistrationException($"Plugin ID '{pluginId}' is reserved for built-in commands.");
        if (pluginId is not null && !definition.Id.StartsWith(pluginId + ".", StringComparison.Ordinal))
            throw new PluginRegistrationException($"Command '{definition.Id}' must start with its plugin ID '{pluginId}.'.");
        if (string.IsNullOrWhiteSpace(definition.Title) || string.IsNullOrWhiteSpace(definition.Category))
            throw new PluginRegistrationException($"Command '{definition.Id}' needs a title and a category.");
        if (definition.DefaultShortcut is not null && !ShortcutText.TryNormalize(definition.DefaultShortcut, out _))
            throw new PluginRegistrationException($"Command '{definition.Id}' has an invalid shortcut '{definition.DefaultShortcut}'.");

        lock (_gate)
        {
            if (_commands.Any(c => c.Id == definition.Id))
                throw new PluginRegistrationException($"Command '{definition.Id}' is already registered.");
            _commands.Add(new RegisteredCommand(definition, handler, pluginId));
        }
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(string id)
    {
        bool removed;
        lock (_gate) removed = _commands.RemoveAll(c => c.Id == id) > 0;
        if (removed)
        {
            CancelRuns(id);
            CommandsChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    public RegisteredCommand? Find(string id)
    {
        lock (_gate) return _commands.FirstOrDefault(c => c.Id == id);
    }

    // A handler is plugin code: a throwing GetState or ExecuteAsync must not take the application down.
#pragma warning disable CA1031
    public CommandState GetState(string id)
    {
        var command = Find(id);
        if (command is null) return new CommandState(CommandAvailability.Hidden, "Unknown command.");
        if (command.Definition.RequiresWorkspace && !HasWorkspace)
            return new CommandState(CommandAvailability.Unavailable, "Open a project first.");
        try
        {
            return command.Handler.GetState(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command {Id} failed to report its state.", id);
            return new CommandState(CommandAvailability.Unavailable, "The command could not check its state.");
        }
    }

    public async Task<CommandRun> ExecuteAsync(string id, object? parameter = null, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        var command = Find(id);
        var state = GetState(id);
        if (command is null || !state.CanRun)
            return Publish(new CommandRun(runId, id, CommandRunStatus.Refused, RefusedBecause: state, Error: state.Reason));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var active = new ActiveRun(id, cts, new CommandRun(runId, id, CommandRunStatus.Running));
        _runs[runId] = active;
        Publish(active.Latest);
        var progress = new Progress<CommandProgressInfo>(p =>
        {
            if (cts.IsCancellationRequested) return;
            active.Latest = Publish(active.Latest with { Progress = p });
        });

        CommandRun result;
        try
        {
            await command.Handler.ExecuteAsync(new Invocation(this, parameter, progress), cts.Token).ConfigureAwait(false);
            result = cts.IsCancellationRequested
                ? new CommandRun(runId, id, CommandRunStatus.Cancelled)
                : new CommandRun(runId, id, CommandRunStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            result = new CommandRun(runId, id, CommandRunStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Id} failed.", id);
            result = new CommandRun(runId, id, CommandRunStatus.Failed, Error: $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _runs.TryRemove(runId, out _);
        }
        return Publish(result);
    }
#pragma warning restore CA1031

    public void CancelRuns(string id)
    {
        foreach (var run in _runs.Values.Where(r => r.CommandId == id)) CancelQuietly(run.Cts);
    }

    public string? GetShortcut(string id)
    {
        lock (_gate) return EffectiveShortcut(id);
    }

    public IReadOnlyList<ShortcutConflict> FindConflicts(string shortcut, string? exceptCommandId = null)
    {
        if (!ShortcutText.TryNormalize(shortcut, out var normalized)) return [];
        var resolved = ShortcutText.Resolve(normalized, _isMac);
        lock (_gate)
        {
            return _commands
                .Where(c => c.Id != exceptCommandId)
                .Select(c => (c.Id, Shortcut: EffectiveShortcut(c.Id)))
                .Where(x => x.Shortcut is not null && ShortcutText.Resolve(x.Shortcut, _isMac) == resolved)
                .Select(x => new ShortcutConflict(x.Id, x.Shortcut!))
                .ToList();
        }
    }

    public bool TrySetShortcut(string id, string? shortcut, out IReadOnlyList<ShortcutConflict> conflicts, bool replaceConflicts = false)
    {
        conflicts = [];
        string? normalized = null;
        if (!string.IsNullOrEmpty(shortcut) && !ShortcutText.TryNormalize(shortcut, out normalized))
            throw new ArgumentException($"'{shortcut}' is not a valid shortcut.", nameof(shortcut));

        var changed = new List<string> { id };
        lock (_gate)
        {
            if (_commands.All(c => c.Id != id)) throw new ArgumentException($"Unknown command '{id}'.", nameof(id));

            if (normalized is not null)
            {
                conflicts = FindConflicts(normalized, id);
                if (conflicts.Count > 0 && !replaceConflicts) return false;
                foreach (var conflict in conflicts)
                {
                    _overrides[conflict.CommandId] = string.Empty;
                    changed.Add(conflict.CommandId);
                }
            }

            if (shortcut is null) _overrides.Remove(id);
            else if (normalized is null) _overrides[id] = string.Empty;
            else if (string.Equals(normalized, DefaultShortcut(id), StringComparison.Ordinal)) _overrides.Remove(id);
            else _overrides[id] = normalized;
        }
        foreach (var changedId in changed) ShortcutsChanged?.Invoke(this, new ShortcutChangedEventArgs(changedId));
        return true;
    }

    public void LoadCustomShortcuts(IReadOnlyDictionary<string, string>? saved)
    {
        lock (_gate)
        {
            _overrides.Clear();
            foreach (var (id, text) in saved ?? new Dictionary<string, string>())
            {
                if (_commands.All(c => c.Id != id)) continue;
                if (text.Length == 0) _overrides[id] = string.Empty;
                else if (ShortcutText.TryNormalize(text, out var normalized)) _overrides[id] = normalized;
            }
        }
        ShortcutsChanged?.Invoke(this, new ShortcutChangedEventArgs(null));
    }

    public string GetTitle(string id, CultureInfo? culture = null) =>
        Find(id) is { } c ? Localize(c.Definition.LocalizedTitles, c.Definition.Title, culture) : id;

    public string GetCategory(string id, CultureInfo? culture = null) =>
        Find(id) is { } c ? Localize(c.Definition.LocalizedCategories, c.Definition.Category, culture) : string.Empty;

    private static string Localize(IReadOnlyDictionary<string, string> table, string fallback, CultureInfo? culture) =>
        LocalizedText.Pick(table, fallback, culture);

    private string? DefaultShortcut(string id) =>
        _commands.FirstOrDefault(c => c.Id == id)?.Definition.DefaultShortcut is { } d && ShortcutText.TryNormalize(d, out var n) ? n : null;

    private string? EffectiveShortcut(string id)
    {
        if (_overrides.TryGetValue(id, out var overridden)) return overridden.Length == 0 ? null : overridden;
        return DefaultShortcut(id);
    }

    private void OnWorkspaceOpened(object? sender, WorkspaceEventArgs e)
    {
        lock (_gate)
        {
            _openWorkspaces.Add(e.WorkspaceId);
            _workspaceId = e.WorkspaceId;
        }
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWorkspaceClosed(object? sender, WorkspaceEventArgs e)
    {
        List<string> toCancel;
        lock (_gate)
        {
            _openWorkspaces.Remove(e.WorkspaceId);
            if (_workspaceId == e.WorkspaceId) _workspaceId = _openWorkspaces.FirstOrDefault();
            toCancel = _openWorkspaces.Count > 0 ? [] : _commands.Where(c => c.Definition.RequiresWorkspace).Select(c => c.Id).ToList();
        }
        foreach (var id in toCancel) CancelRuns(id);
        CommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void CancelQuietly(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended first.
        }
    }

    private CommandRun Publish(CommandRun run)
    {
        RunChanged?.Invoke(this, new CommandRunEventArgs(run));
        return run;
    }

    [GeneratedRegex("^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdPattern();

    private sealed class ActiveRun(string commandId, CancellationTokenSource cts, CommandRun latest)
    {
        public string CommandId { get; } = commandId;
        public CancellationTokenSource Cts { get; } = cts;
        public CommandRun Latest { get; set; } = latest;
    }

    private sealed record Invocation(ICommandContext Context, object? Parameter, IProgress<CommandProgressInfo> Progress) : ICommandInvocation;
}
