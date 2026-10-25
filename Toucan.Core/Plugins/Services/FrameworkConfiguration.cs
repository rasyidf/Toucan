using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>Detached project options using the same schema validation and form contract as plugins.</summary>
public sealed class FrameworkConfiguration(ConfigSchema schema, FrameworkOptions options, string projectPath) : IPluginConfiguration
{
    public ConfigSchema Schema => schema;
    public FrameworkOptions Options { get; private set; } = options;
    public event EventHandler<ConfigChangedEventArgs>? Changed;
    private ConfigField Field(string key) => schema.Fields.FirstOrDefault(f => f.Key == key) ?? throw new ArgumentException("Unsupported framework setting.", nameof(key));
    public T? GetValue<T>(string key, ConfigTarget? target = null)
    {
        Field(key);
        return JsonSerializer.SerializeToElement(Options, JsonSerializerOptions.Web).GetProperty(key).Deserialize<T>();
    }
    public ConfigValidationResult Validate(string key, object? value) => PluginConfiguration.Validate(Field(key), value ?? Field(key).Default ?? "");
    public Task<ConfigValidationResult> SetAsync(string key, object? value, ConfigTarget? target = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Validate(key, value);
        if (result.IsValid)
        {
            var next = value ?? Field(key).Default;
            Options = key switch { "defaultPackage" => Options with { DefaultPackage = (string)next! },
                "resolveNamespaces" => Options with { ResolveNamespaces = (bool)next! }, "keyPrefix" => Options with { KeyPrefix = (string)next! },
                _ => Options };
            Changed?.Invoke(this, new(key, ConfigTarget.ForWorkspace(projectPath)));
        }
        return Task.FromResult(result);
    }
    public Task<string?> GetSecretAsync(string key, ConfigTarget? target = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
