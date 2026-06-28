using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class AuditService(ILogger<AuditService> logger) : IAuditService
{
    private const string SidecarFileName = ".toucan-metadata.json";
    private const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Dictionary<(string Language, string Namespace), AuditMetadata> _metadata = [];

    public void RecordSave(TranslationItem item)
    {
        var key = (item.Language, item.Namespace);
        var metadata = GetOrCreateMetadata(key);
        metadata.LastModifiedUtc = DateTime.UtcNow;
        item.LastModifiedUtc = metadata.LastModifiedUtc;
    }

    public void RecordApproval(TranslationItem item)
    {
        var key = (item.Language, item.Namespace);
        var metadata = GetOrCreateMetadata(key);
        metadata.ApprovedAtUtc = DateTime.UtcNow;
        item.ApprovedAtUtc = metadata.ApprovedAtUtc;
    }

    public void SetChangeType(TranslationItem item, ChangeType type)
    {
        var key = (item.Language, item.Namespace);
        var metadata = GetOrCreateMetadata(key);
        metadata.ChangeType = type;
        item.ChangeType = type;
    }

    public AuditMetadata? GetMetadata(TranslationItem item)
    {
        var key = (item.Language, item.Namespace);
        return _metadata.TryGetValue(key, out var metadata) ? metadata : null;
    }

    public void LoadFromSidecar(string folder)
    {
        _metadata.Clear();

        var filePath = Path.Combine(folder, SidecarFileName);
        if (!File.Exists(filePath))
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Audit sidecar file not found at {Path}, starting with empty metadata", filePath);
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var sidecar = JsonSerializer.Deserialize<SidecarDocument>(json, s_readOptions);

            if (sidecar?.Metadata is null)
            {
                logger.LogWarning("Audit sidecar at {Path} has no metadata section, starting with empty metadata", filePath);
                return;
            }

            foreach (var (language, entries) in sidecar.Metadata)
            {
                foreach (var (ns, entry) in entries)
                {
                    _metadata[(language, ns)] = new AuditMetadata
                    {
                        LastModifiedUtc = entry.LastModifiedUtc,
                        ApprovedAtUtc = entry.ApprovedAtUtc,
                        ChangeType = entry.ChangeType
                    };
                }
            }

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Loaded {Count} audit metadata entries from sidecar", _metadata.Count);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed audit sidecar at {Path}, starting with empty metadata", filePath);
            _metadata.Clear();
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read audit sidecar at {Path}, starting with empty metadata", filePath);
            _metadata.Clear();
        }
    }

    public void SaveToSidecar(string folder)
    {
        var filePath = Path.Combine(folder, SidecarFileName);

        var sidecar = new SidecarDocument
        {
            SchemaVersion = SchemaVersion,
            Metadata = []
        };

        foreach (var ((language, ns), metadata) in _metadata)
        {
            if (!sidecar.Metadata.TryGetValue(language, out var languageEntries))
            {
                languageEntries = [];
                sidecar.Metadata[language] = languageEntries;
            }

            languageEntries[ns] = new SidecarEntry
            {
                LastModifiedUtc = metadata.LastModifiedUtc,
                ApprovedAtUtc = metadata.ApprovedAtUtc,
                ChangeType = metadata.ChangeType
            };
        }

        var json = JsonSerializer.Serialize(sidecar, s_jsonOptions);
        File.WriteAllText(filePath, json);
    }

    public void Clear()
    {
        _metadata.Clear();
    }

    private AuditMetadata GetOrCreateMetadata((string Language, string Namespace) key)
    {
        if (!_metadata.TryGetValue(key, out var metadata))
        {
            metadata = new AuditMetadata();
            _metadata[key] = metadata;
        }

        return metadata;
    }

    // Internal JSON models for sidecar serialization
    private sealed class SidecarDocument
    {
        public string SchemaVersion { get; set; } = "1.0";
        public Dictionary<string, Dictionary<string, SidecarEntry>> Metadata { get; set; } = [];
    }

    private sealed class SidecarEntry
    {
        public DateTime? LastModifiedUtc { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public ChangeType ChangeType { get; set; } = ChangeType.DirectEdit;
    }
}
