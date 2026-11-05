using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Core.Models;

/// <summary>
/// One connection from this project to a content source, saved in <c>toucan.tproj</c> under <c>sources</c>. Holds no
/// credentials: each user signs in with their own account, so teammates can share the file.
/// </summary>
/// <remarks>
/// A connection is owned either by a content-source adapter (<see cref="Adapter"/>, the usual case) or directly by a
/// plugin that manages its own connection activator (<see cref="Plugin"/>). Only the owner's activators run for it.
/// </remarks>
public sealed class SourceConnectionSettings
{
    /// <summary>Stable ID, unique in the project. Also the scope of the plugin's connection settings and secrets.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name; defaults to the remote project's name.</summary>
    public string? Name { get; set; }

    /// <summary>The content-source adapter ID, e.g. <c>acme.lokalise.api</c>.</summary>
    public string? Adapter { get; set; }

    /// <summary>For a plugin-managed connection with no adapter: the plugin whose connection activators run.</summary>
    public string? Plugin { get; set; }

    /// <summary>The service address; null uses the adapter's default.</summary>
    public Uri? BaseUrl { get; set; }

    public SourceRemoteProject? RemoteProject { get; set; }

    /// <summary><c>mirror</c> (local files primary) or <c>primary</c> (the service is primary, Toucan caches).</summary>
    public string Role { get; set; } = SourceRoles.Mirror;

    /// <summary>Project language → remote language code. Only mapped languages are read or written.</summary>
    public Dictionary<string, string> Languages { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Remote resources in this connection and the package each maps to. Empty means one flat resource.</summary>
    public List<SourceResourceMap> Resources { get; set; } = [];

    public SourceKeyMapping Keys { get; set; } = new();

    public SourceSyncPolicy Policy { get; set; } = new();

    /// <summary>Values for the adapter's own connection settings (non-secret).</summary>
    public Dictionary<string, JsonElement> Settings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The plugin that owns the connection: the adapter's plugin when known, else <see cref="Plugin"/>.</summary>
    [JsonIgnore]
    public bool HasOwner => !string.IsNullOrWhiteSpace(Adapter) || !string.IsNullOrWhiteSpace(Plugin);

    public SourceConnectionSettings Clone() => JsonSerializer.Deserialize<SourceConnectionSettings>(JsonSerializer.Serialize(this))!;
}

public static class SourceRoles
{
    public const string Mirror = "mirror";
    public const string Primary = "primary";
}

public sealed class SourceRemoteProject
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
}

/// <summary>A remote resource and the project package it maps to (null: the project's only or default package).</summary>
public sealed class SourceResourceMap
{
    public string Remote { get; set; } = string.Empty;
    public string? Package { get; set; }
}

/// <summary>How remote key names map to namespace and key paths.</summary>
public sealed class SourceKeyMapping
{
    /// <summary>Joins path segments into a remote key name. A segment that contains it is escaped with a backslash.</summary>
    public string Separator { get; set; } = ".";

    /// <summary>Whether remote key names differing only in case are different keys.</summary>
    public bool CaseSensitive { get; set; } = true;
}

/// <summary>What a connection may do on its own.</summary>
public sealed class SourceSyncPolicy
{
    /// <summary>Open the connection when the project opens instead of on first use.</summary>
    public bool ConnectOnOpen { get; set; }

    public bool PushStatuses { get; set; } = true;
    public bool PushComments { get; set; }

    /// <summary><c>ask</c> (list local deletions, push only when included), <c>never</c>, or <c>always</c>.</summary>
    public string DeleteRemote { get; set; } = "ask";
}
