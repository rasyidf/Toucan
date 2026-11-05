using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Core.Services;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>The remote identity a unit is bound to.</summary>
public sealed record SyncRemoteKey(string ResourceId, string Name, string? RemoteId = null)
{
    public RemoteKey ToRemote() => new(ResourceId, Name, RemoteId);
}

/// <summary>What both sides last agreed on for one translation. The common ancestor of the three-way comparison.</summary>
public sealed class BaselineUnit
{
    public Guid EntryId { get; set; }
    public string Language { get; set; } = string.Empty;
    public SyncRemoteKey Remote { get; set; } = new(string.Empty, string.Empty);
    public string? RemoteRevision { get; set; }
    public string ValueHash { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>The remote status meaning last agreed on, or null when statuses are not synced.</summary>
    public SourceReviewMeaning? Status { get; set; }
    public string? CommentHash { get; set; }
    public DateTimeOffset SyncedAt { get; set; }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));
}

public sealed class BaselineDocument
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// <summary>Per connection, per machine. Never committed.</summary>
public sealed class SyncBaseline
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public List<BaselineUnit> Units { get; set; } = [];
    public List<BaselineDocument> Documents { get; set; } = [];
    public DateTimeOffset? LastFullRead { get; set; }
    public string? DeltaCursor { get; set; }
    public DateTimeOffset? LastPull { get; set; }
    public DateTimeOffset? LastPush { get; set; }
}

public enum JournalState
{
    Planned,
    InFlight,
    Confirmed,
    Failed,
    Unknown,
    Conflict,
}

public enum JournalKind
{
    PutValue,
    PutStatus,
    PutComment,
    CreateKey,
    RenameKey,
    DeleteKey,
    DeleteTranslation,
}

/// <summary>One outbound operation, written before it is sent so a restart can find out what happened to it.</summary>
public sealed class JournalEntry
{
    public Guid OperationId { get; set; }
    public JournalKind Kind { get; set; }
    public Guid EntryId { get; set; }
    public string Language { get; set; } = string.Empty;

    /// <summary>The service's code for <see cref="Language"/>.</summary>
    public string RemoteLanguage { get; set; } = string.Empty;

    /// <summary>Every project translation a create or key-level operation covers.</summary>
    public List<JournalMember> Members { get; set; } = [];
    public SyncRemoteKey Target { get; set; } = new(string.Empty, string.Empty);

    /// <summary>The intended value (or new name for a rename); for a create, the values by remote language are in <see cref="Values"/>.</summary>
    public string? Payload { get; set; }
    public SourceReviewMeaning? Status { get; set; }
    public Dictionary<string, string>? Values { get; set; }
    public string? ExpectedRevision { get; set; }

    /// <summary>The baseline value when the operation was planned, used to tell "not applied" from "applied by someone else".</summary>
    public string? BaseValue { get; set; }
    public JournalState State { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string? JobId { get; set; }
    public string? ResultRevision { get; set; }
    public string? AssignedRemoteId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class JournalMember
{
    public Guid EntryId { get; set; }
    public string Language { get; set; } = string.Empty;
}

public sealed class SyncJournal
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public List<JournalEntry> Entries { get; set; } = [];
}

/// <summary>
/// Reads and writes a connection's sync state under <c>.toucan/local/sources/{connectionId}/</c>: <c>baseline.json</c>,
/// <c>journal.json</c> and, for a primary connection, <c>cache/</c>. Writes are atomic. A file that cannot be read, or that a
/// newer Toucan wrote, is moved aside and treated as missing, so the connection falls back to first-sync mode instead of
/// overwriting anything.
/// </summary>
public sealed class SyncStateStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _folder;
    private readonly TimeProvider _time;

    public SyncStateStore(string projectRoot, string connectionId, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (connectionId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 || connectionId is "." or "..")
            throw new ArgumentException("The connection ID cannot be used as a folder name.", nameof(connectionId));
        LocalRoot = Path.Combine(Path.GetFullPath(projectRoot), ".toucan", "local");
        _folder = Path.Combine(LocalRoot, "sources", connectionId);
        _time = time ?? TimeProvider.System;
    }

    public string LocalRoot { get; }
    public string Folder => _folder;
    public string BaselinePath => Path.Combine(_folder, "baseline.json");
    public string JournalPath => Path.Combine(_folder, "journal.json");

    /// <summary>The folder a primary connection caches the service's files in.</summary>
    public string CacheFolder => Path.Combine(_folder, "cache");

    /// <summary>Whether a baseline was found; false means the next plan is a first sync.</summary>
    public bool HasBaseline => File.Exists(BaselinePath);

    /// <summary>Creates the folder and the ignore file that keeps it out of version control.</summary>
    public void EnsureFolder()
    {
        Directory.CreateDirectory(_folder);
        var ignore = Path.Combine(LocalRoot, ".gitignore");
        if (!File.Exists(ignore)) AtomicFile.WriteAllText(ignore, "*\n");
    }

    public SyncBaseline? LoadBaseline() => Load<SyncBaseline>(BaselinePath, b => b.Schema);

    public SyncJournal LoadJournal() => Load<SyncJournal>(JournalPath, j => j.Schema) ?? new SyncJournal();

    public void SaveBaseline(SyncBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        EnsureFolder();
        baseline.Schema = SyncBaseline.CurrentSchema;
        AtomicFile.WriteAllText(BaselinePath, JsonSerializer.Serialize(baseline, s_json));
    }

    public void SaveJournal(SyncJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        EnsureFolder();
        journal.Schema = SyncJournal.CurrentSchema;
        AtomicFile.WriteAllText(JournalPath, JsonSerializer.Serialize(journal, s_json));
    }

    /// <summary>Role <c>primary</c>: the units last pulled, so the project opens offline.</summary>
    public string UnitCachePath => Path.Combine(CacheFolder, "units.json");

    public void SaveUnitCache(IEnumerable<RemoteUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        EnsureFolder();
        Directory.CreateDirectory(CacheFolder);
        AtomicFile.WriteAllText(UnitCachePath, JsonSerializer.Serialize(units.ToList(), s_json));
    }

    public IReadOnlyList<RemoteUnit>? LoadUnitCache()
    {
        try { return JsonSerializer.Deserialize<List<RemoteUnit>>(File.ReadAllText(UnitCachePath), s_json); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { Quarantine(UnitCachePath); return null; }
    }

    /// <summary>Forgets the connection's state (sign-out and remove).</summary>
    public void Delete()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private T? Load<T>(string path, Func<T, int> schema) where T : class
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, s_json);
            if (value is not null && schema(value) <= (typeof(T) == typeof(SyncBaseline) ? SyncBaseline.CurrentSchema : SyncJournal.CurrentSchema)) return value;
        }
        catch (JsonException) { }
        Quarantine(path);
        return null;
    }

    private void Quarantine(string path)
    {
        var aside = $"{path}.corrupt-{_time.GetUtcNow():yyyyMMddHHmmss}";
        try { File.Move(path, File.Exists(aside) ? aside + "-" + Guid.NewGuid().ToString("N")[..6] : aside); }
        catch (IOException) { }
    }
}
