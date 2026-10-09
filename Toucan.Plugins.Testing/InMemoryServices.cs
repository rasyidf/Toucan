using System.Collections.Concurrent;
using System.Text.Json;

namespace Toucan.Plugins.Testing;

/// <summary>Everything a plugin may call, kept in memory (storage in a temporary folder), with what it did recorded for assertions.</summary>
public sealed class TestPluginServices : IPluginServices, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-plugin-test-" + Guid.NewGuid().ToString("N"));

    public TestPluginServices()
    {
        Directory.CreateDirectory(_root);
        Storage = new TestStorage(_root);
        Secrets = new TestSecrets();
        Configuration = new TestConfiguration(Secrets);
        Notifier = new RecordingNotifier();
        Operations = new TestOperations();
        Diagnostics = new TestDiagnostics();
        Workspace = new InMemoryWorkspace();
    }

    public IPluginStorage Storage { get; }
    public IPluginConfiguration Configuration { get; }
    public IPluginSecrets Secrets { get; }
    public IPluginNotifier Notifier { get; }
    public IBackgroundOperations Operations { get; }
    public IPluginDiagnostics Diagnostics { get; }
    public IWorkspaceApi Workspace { get; }

    public RecordingNotifier Notifications => (RecordingNotifier)Notifier;
    public TestConfiguration Config => (TestConfiguration)Configuration;
    public TestOperations BackgroundOperations => (TestOperations)Operations;
    public TestDiagnostics DiagnosticLog => (TestDiagnostics)Diagnostics;
    public InMemoryWorkspace InMemory => (InMemoryWorkspace)Workspace;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a temporary folder left behind is harmless */ }
    }
}

internal sealed class TestStorage(string root) : IPluginStorage
{
    public string DataDirectory { get; } = root;

    public string ResolvePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
        if (!full.StartsWith(Path.GetFullPath(DataDirectory), StringComparison.Ordinal))
            throw new ArgumentException("The path leaves the plugin's data folder.", nameof(relativePath));
        return full;
    }

    public bool Exists(string relativePath) => File.Exists(ResolvePath(relativePath));

    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(relativePath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default) =>
        await ReadTextAsync(relativePath, cancellationToken).ConfigureAwait(false) is { } text ? JsonSerializer.Deserialize<T>(text) : default;

    public Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) =>
        WriteTextAsync(relativePath, JsonSerializer.Serialize(value), cancellationToken);

    public bool Delete(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}

internal sealed class TestSecrets : IPluginSecrets
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    private static string Key(string name, ConfigTarget? target) =>
        $"{target?.Scope}|{target?.WorkspaceId}|{target?.ConnectionId}|{name}";

    public Task<string?> GetAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(Key(name, target), out var v) ? v : null);

    public Task SetAsync(string name, string? value, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        if (value is null) _values.TryRemove(Key(name, target), out _);
        else _values[Key(name, target)] = value;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string name, ConfigTarget? target = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryRemove(Key(name, target), out _));
}

/// <summary>Typed settings with the schema's defaults and validation. Migrations are not run: the harness always starts from the current schema.</summary>
public sealed class TestConfiguration(IPluginSecrets secrets) : IPluginConfiguration
{
    private readonly ConcurrentDictionary<string, object?> _values = new();

    public ConfigSchema? Schema { get; set; }

    public event EventHandler<ConfigChangedEventArgs>? Changed;

    private ConfigField Field(string key) =>
        Schema?.Fields.FirstOrDefault(f => f.Key == key) ?? throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));

    private static string Slot(string key, ConfigTarget target) => $"{target.Scope}|{target.WorkspaceId}|{target.ConnectionId}|{key}";

    public T? GetValue<T>(string key, ConfigTarget? target = null)
    {
        var field = Field(key);
        var resolved = target ?? new ConfigTarget(field.Scope);
        var value = _values.TryGetValue(Slot(key, resolved), out var v) ? v : field.Default;
        if (value is null) return default;
        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public ConfigValidationResult Validate(string key, object? value)
    {
        var field = Field(key);
        if (value is null) return field.Required && field.Default is null ? ConfigValidationResult.Fail($"{field.Label} is required.") : ConfigValidationResult.Ok;
        if (value is string s && field.Pattern is { Length: > 0 } p && !System.Text.RegularExpressions.Regex.IsMatch(s, p))
            return ConfigValidationResult.Fail($"{field.Label} is not in the expected format.");
        if (field.Type == ConfigFieldType.Choice && !field.Choices.Any(c => c.Value == value.ToString()))
            return ConfigValidationResult.Fail($"{field.Label} must be one of the listed choices.");
        if (value is IConvertible && field.Type is ConfigFieldType.WholeNumber or ConfigFieldType.Number)
        {
            var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            if (field.Minimum is { } min && number < min) return ConfigValidationResult.Fail($"{field.Label} must be at least {min}.");
            if (field.Maximum is { } max && number > max) return ConfigValidationResult.Fail($"{field.Label} must be at most {max}.");
        }
        return ConfigValidationResult.Ok;
    }

    public async Task<ConfigValidationResult> SetAsync(string key, object? value, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        var field = Field(key);
        var resolved = target ?? new ConfigTarget(field.Scope);
        var result = Validate(key, value);
        if (!result.IsValid) return result;

        if (field.Type == ConfigFieldType.Secret) await secrets.SetAsync(key, value as string, resolved, cancellationToken).ConfigureAwait(false);
        else if (value is null) _values.TryRemove(Slot(key, resolved), out _);
        else _values[Slot(key, resolved)] = value;
        Changed?.Invoke(this, new ConfigChangedEventArgs(key, resolved));
        return ConfigValidationResult.Ok;
    }

    public Task<string?> GetSecretAsync(string key, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        var field = Field(key);
        return secrets.GetAsync(key, target ?? new ConfigTarget(field.Scope), cancellationToken);
    }
}

/// <summary>Collects notifications so a test can assert on them.</summary>
public sealed class RecordingNotifier : IPluginNotifier
{
    private readonly ConcurrentQueue<PluginNotification> _items = new();
    public IReadOnlyList<PluginNotification> Items => [.. _items];
    public void Notify(PluginNotification notification) => _items.Enqueue(notification);
}

/// <summary>Runs operations on the thread pool. <see cref="CancelAll"/> stands in for the workspace closing.</summary>
public sealed class TestOperations : IBackgroundOperations
{
    private readonly ConcurrentDictionary<Guid, Handle> _handles = new();

    public IReadOnlyList<IOperationHandle> Active => [.. _handles.Values.Where(h => h.Status == OperationStatus.Running)];
    public IReadOnlyList<IOperationHandle> All => [.. _handles.Values];

    public IOperationHandle Start(OperationOptions options, Func<IOperationContext, CancellationToken, Task> work)
    {
        var handle = new Handle(options, work);
        _handles[handle.Id] = handle;
        return handle;
    }

    public void CancelAll()
    {
        foreach (var handle in _handles.Values.Where(h => h.Options.CancelWithWorkspace)) handle.Cancel();
    }

    private sealed class Handle : IOperationHandle, IOperationContext
    {
        private readonly CancellationTokenSource _cts = new();

        public Handle(OperationOptions options, Func<IOperationContext, CancellationToken, Task> work)
        {
            Options = options;
            Completion = Task.Run(async () =>
            {
                try
                {
                    await work(this, _cts.Token).ConfigureAwait(false);
                    Status = OperationStatus.Completed;
                }
                catch (OperationCanceledException)
                {
                    Status = OperationStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    FailureMessage = ex.Message;
                    Status = OperationStatus.Failed;
                }
            });
        }

        public OperationOptions Options { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public string Title => Options.Title;
        public OperationStatus Status { get; private set; } = OperationStatus.Running;
        public string? FailureMessage { get; private set; }
        public Task Completion { get; }
        public void Cancel() { if (Options.IsCancellable) _cts.Cancel(); }
        public void Report(string? message, double? fraction = null) { }
    }
}

/// <summary>Masks registered secrets and keeps what the plugin wrote.</summary>
public sealed class TestDiagnostics : IPluginDiagnostics
{
    private readonly ConcurrentBag<string> _secrets = [];
    private readonly ConcurrentQueue<(DiagnosticLevel Level, string Message)> _lines = new();

    public IReadOnlyList<(DiagnosticLevel Level, string Message)> Lines => [.. _lines];

    public string Redact(string text)
    {
        foreach (var secret in _secrets.Where(s => s.Length > 0)) text = text.Replace(secret, "***", StringComparison.Ordinal);
        return text;
    }

    public void Write(DiagnosticLevel level, string message) => _lines.Enqueue((level, Redact(message)));
    public void RegisterSecret(string value) => _secrets.Add(value);
}
