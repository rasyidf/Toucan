using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Services;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// A plugin's settings, kept in <c>config.json</c> in its data folder: <c>{ schemaVersion, app, workspaces, connections }</c>,
/// each a bag of values. Secret fields are not in the file; they live in the secret store.
/// </summary>
internal sealed class PluginConfiguration(string pluginId, IPluginStorage storage, PluginSecrets secrets, IDiagnosticsService diagnostics) : IPluginConfiguration
{
    private const string FileName = "config.json";
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private ConfigSchema? _schema;
    private State? _state;

    public ConfigSchema? Schema { get { lock (_gate) return _schema; } }

    public event EventHandler<ConfigChangedEventArgs>? Changed;

    /// <summary>Sets the schema once, and checks it for mistakes a plugin author can fix.</summary>
    public void SetSchema(ConfigSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var problems = CheckSchema(schema).ToList();
        if (problems.Count > 0) throw new PluginRegistrationException($"Plugin '{pluginId}' has an invalid configuration schema: {string.Join(" ", problems)}");
        lock (_gate)
        {
            if (_schema is not null) throw new PluginRegistrationException($"Plugin '{pluginId}' set its configuration schema twice.");
            _schema = schema;
        }
    }

    // ─── reading ───

    public T? GetValue<T>(string key, ConfigTarget? target = null)
    {
        var (field, resolved) = Resolve(key, target);
        if (field.Type == ConfigFieldType.Secret) return default;

        object? value;
        lock (_gate)
        {
            var bag = Load().Bag(resolved, create: false);
            value = bag is not null && bag.TryGetValue(key, out var stored) && stored is not null && Validate(field, stored).IsValid ? stored : field.Default;
        }
        return value is null ? default : Convert<T>(value);
    }

    public async Task<string?> GetSecretAsync(string key, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        var (field, resolved) = Resolve(key, target);
        if (field.Type != ConfigFieldType.Secret) throw new ArgumentException($"'{key}' is not a secret setting.", nameof(key));
        return await secrets.GetAsync(key, resolved, cancellationToken).ConfigureAwait(false);
    }

    // ─── writing ───

    public ConfigValidationResult Validate(string key, object? value)
    {
        var field = Field(key);
        return value is null ? (field.Required && field.Default is null ? ConfigValidationResult.Fail($"{field.Label} is required.") : ConfigValidationResult.Ok) : Validate(field, value);
    }

    public async Task<ConfigValidationResult> SetAsync(string key, object? value, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        var (field, resolved) = Resolve(key, target);
        var result = Validate(key, value);
        if (!result.IsValid) return result;

        if (field.Type == ConfigFieldType.Secret)
        {
            await secrets.SetAsync(key, value as string, resolved, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string json;
                lock (_gate)
                {
                    var state = Load();
                    var bag = state.Bag(resolved, create: true)!;
                    if (value is null) bag.Remove(key);
                    else bag[key] = Normalize(field, value);
                    json = state.Serialize((_schema?.Version) ?? state.Version);
                }
                await storage.WriteTextAsync(FileName, json, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        Changed?.Invoke(this, new ConfigChangedEventArgs(key, resolved));
        return ConfigValidationResult.Ok;
    }

    // ─── loading and migration ───

    private State Load()
    {
        if (_state is not null) return _state;

        State state;
        var path = storage.ResolvePath(FileName);
        try
        {
            state = File.Exists(path) ? State.Parse(File.ReadAllText(path)) : new State(_schema?.Version ?? 1);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unreadable file must not stop the plugin; start from defaults and say so where support can see it.
            diagnostics.Write(pluginId, DiagnosticLevel.Warning, $"Settings could not be read and were reset to defaults: {ex.Message}");
            state = new State(_schema?.Version ?? 1);
        }

        if (_schema is { } schema && state.Version < schema.Version)
        {
            Migrate(state, schema);
            // Write the migrated form now, so an old file is never migrated twice.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AtomicFile.WriteAllText(path, state.Serialize(schema.Version));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Write(pluginId, DiagnosticLevel.Warning, $"Migrated settings could not be saved: {ex.Message}");
            }
        }
        return _state = state;
    }

    private void Migrate(State state, ConfigSchema schema)
    {
        while (state.Version < schema.Version)
        {
            var step = schema.Migrations.FirstOrDefault(m => m.FromVersion == state.Version);
            if (step is not null)
            {
                foreach (var (scope, bag) in state.AllBags()) step.Apply(scope, bag);
            }
            else
            {
                diagnostics.Write(pluginId, DiagnosticLevel.Warning,
                    $"No settings migration from version {state.Version}; values that no longer fit fall back to their defaults.");
            }
            state.Version++;
        }
    }

    // ─── field rules ───

    private ConfigField Field(string key) =>
        Schema?.Fields.FirstOrDefault(f => f.Key == key) ?? throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));

    private (ConfigField Field, ConfigTarget Target) Resolve(string key, ConfigTarget? target)
    {
        var field = Field(key);
        var resolved = target ?? new ConfigTarget(field.Scope);
        if (resolved.Scope != field.Scope)
            throw new ArgumentException($"'{key}' is a {field.Scope} setting, not a {resolved.Scope} one.", nameof(target));
        if (field.Scope == ConfigScope.Workspace && string.IsNullOrWhiteSpace(resolved.WorkspaceId))
            throw new ArgumentException($"'{key}' needs a project: use ConfigTarget.ForWorkspace.", nameof(target));
        if (field.Scope == ConfigScope.Connection && string.IsNullOrWhiteSpace(resolved.ConnectionId))
            throw new ArgumentException($"'{key}' needs a connection: use ConfigTarget.ForConnection.", nameof(target));
        return (field, resolved);
    }

    private static ConfigValidationResult Validate(ConfigField field, object value)
    {
        switch (field.Type)
        {
            case ConfigFieldType.Boolean:
                return value is bool ? ConfigValidationResult.Ok : ConfigValidationResult.Fail($"{field.Label} must be on or off.");

            case ConfigFieldType.WholeNumber or ConfigFieldType.Number:
                if (!TryNumber(value, out var number, wholeOnly: field.Type == ConfigFieldType.WholeNumber))
                    return ConfigValidationResult.Fail($"{field.Label} must be {(field.Type == ConfigFieldType.WholeNumber ? "a whole number" : "a number")}.");
                if (field.Minimum is { } min && number < min) return ConfigValidationResult.Fail($"{field.Label} must be at least {min.ToString(CultureInfo.InvariantCulture)}.");
                if (field.Maximum is { } max && number > max) return ConfigValidationResult.Fail($"{field.Label} must be at most {max.ToString(CultureInfo.InvariantCulture)}.");
                return ConfigValidationResult.Ok;

            default:
                if (value is not string text) return ConfigValidationResult.Fail($"{field.Label} must be text.");
                if (field.Required && string.IsNullOrWhiteSpace(text)) return ConfigValidationResult.Fail($"{field.Label} is required.");
                if (field.Minimum is { } minLength && text.Length < minLength) return ConfigValidationResult.Fail($"{field.Label} must have at least {minLength:0} characters.");
                if (field.Maximum is { } maxLength && text.Length > maxLength) return ConfigValidationResult.Fail($"{field.Label} must have at most {maxLength:0} characters.");
                if (field.Pattern is { Length: > 0 } pattern && text.Length > 0 && !Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                    return ConfigValidationResult.Fail($"{field.Label} is not in the expected format.");
                if (field.Type == ConfigFieldType.Choice && !field.Choices.Any(c => c.Value == text))
                    return ConfigValidationResult.Fail($"{field.Label} must be one of: {string.Join(", ", field.Choices.Select(c => c.Label))}.");
                if (field.Type == ConfigFieldType.Url && text.Length > 0
                    && !(Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                    return ConfigValidationResult.Fail($"{field.Label} must be a web address starting with http:// or https://.");
                return ConfigValidationResult.Ok;
        }
    }

    private static bool TryNumber(object value, out double number, bool wholeOnly)
    {
        number = 0;
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            case ulong u:
                number = u;
                return true;
            case float or double or decimal:
                number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !wholeOnly || number == Math.Floor(number);
            default:
                return false;
        }
    }

    /// <summary>The form a value is stored in: long for whole numbers, double for numbers.</summary>
    private static object Normalize(ConfigField field, object value) => field.Type switch
    {
        ConfigFieldType.WholeNumber => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
        ConfigFieldType.Number => System.Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => value,
    };

    private static T? Convert<T>(object value)
    {
        if (value is T direct) return direct;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            return (T)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidCastException($"The setting holds {value.GetType().Name}, not {typeof(T).Name}.", ex);
        }
    }

    private static IEnumerable<string> CheckSchema(ConfigSchema schema)
    {
        if (schema.Version < 1) yield return "Version must be 1 or more.";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || !field.Key.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')) yield return $"'{field.Key}' is not a valid key.";
            else if (!keys.Add(field.Key)) yield return $"'{field.Key}' is defined twice.";
            if (field.Type == ConfigFieldType.Choice && field.Choices.Count == 0) yield return $"'{field.Key}' is a choice with no choices.";
            if (field.Type == ConfigFieldType.Secret && field.Default is not null) yield return $"'{field.Key}' is a secret and cannot have a default.";
            if (field.Pattern is { Length: > 0 } p)
            {
                string? error = null;
                try { _ = new Regex(p, RegexOptions.None, TimeSpan.FromSeconds(1)); }
                catch (ArgumentException ex) { error = ex.Message; }
                if (error is not null) yield return $"'{field.Key}' has an invalid pattern: {error}";
            }
            if (field.Default is { } d && field.Type != ConfigFieldType.Secret && !Validate(field with { Required = false }, d).IsValid)
                yield return $"The default of '{field.Key}' does not pass its own rules.";
        }
        if (schema.Migrations.GroupBy(m => m.FromVersion).Any(g => g.Count() > 1)) yield return "Two migrations start from the same version.";
    }

    // ─── file state ───

    private sealed class State(int version)
    {
        public int Version { get; set; } = version;
        public Dictionary<string, object?> App { get; } = [];
        public Dictionary<string, Dictionary<string, object?>> Workspaces { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, object?>> Connections { get; } = new(StringComparer.Ordinal);

        private static string WorkspaceKey(string workspaceId) => SecretKeys.ProjectId(workspaceId);

        private static string ConnectionKey(ConfigTarget t) =>
            (string.IsNullOrWhiteSpace(t.WorkspaceId) ? "app" : WorkspaceKey(t.WorkspaceId)) + "/" + t.ConnectionId!.Trim().ToLowerInvariant();

        public Dictionary<string, object?>? Bag(ConfigTarget target, bool create)
        {
            switch (target.Scope)
            {
                case ConfigScope.App:
                    return App;
                case ConfigScope.Workspace:
                    return Get(Workspaces, WorkspaceKey(target.WorkspaceId!), create);
                default:
                    return Get(Connections, ConnectionKey(target), create);
            }
        }

        private static Dictionary<string, object?>? Get(Dictionary<string, Dictionary<string, object?>> all, string key, bool create)
        {
            if (all.TryGetValue(key, out var bag)) return bag;
            return create ? all[key] = [] : null;
        }

        public IEnumerable<(ConfigScope Scope, Dictionary<string, object?> Bag)> AllBags()
        {
            yield return (ConfigScope.App, App);
            foreach (var bag in Workspaces.Values) yield return (ConfigScope.Workspace, bag);
            foreach (var bag in Connections.Values) yield return (ConfigScope.Connection, bag);
        }

        public string Serialize(int schemaVersion) => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = schemaVersion,
            ["app"] = App,
            ["workspaces"] = Workspaces,
            ["connections"] = Connections,
        }, s_json);

        public static State Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("The settings file is not an object.");

            var state = new State(root.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var version) ? version : 1);
            if (root.TryGetProperty("app", out var app)) Fill(state.App, app);
            if (root.TryGetProperty("workspaces", out var ws)) FillAll(state.Workspaces, ws);
            if (root.TryGetProperty("connections", out var cn)) FillAll(state.Connections, cn);
            return state;
        }

        private static void FillAll(Dictionary<string, Dictionary<string, object?>> into, JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                var bag = new Dictionary<string, object?>();
                Fill(bag, property.Value);
                into[property.Name] = bag;
            }
        }

        private static void Fill(Dictionary<string, object?> into, JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                into[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                    _ => null,
                };
            }
        }
    }
}
