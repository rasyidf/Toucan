using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Outcome of restoring the files a save touched.</summary>
/// <param name="Restored">Files put back to their pre-save content (or removed, when the save created them).</param>
/// <param name="Failed">Files that could not be restored, with the reason. The journal is kept so the user can retry.</param>
public sealed record SaveRollbackResult(IReadOnlyList<string> Restored, IReadOnlyList<(string File, string Reason)> Failed)
{
    public bool Complete => Failed.Count == 0;
}

/// <summary>
/// Makes a multi-file save recoverable. Before anything is written, every file the save may touch is copied under
/// <c>.toucan/recovery/pending</c> together with a journal. If the save fails the files are restored; if the process
/// dies mid-save, the journal is still there on the next open (<see cref="FindInterrupted"/>). A committed save keeps
/// one generation of originals in <c>.toucan/recovery/previous</c>.
/// </summary>
public sealed class SaveTransaction
{
    private const string PendingName = "pending";
    private const string PreviousName = "previous";
    private const string JournalFile = "journal.toucan-journal";

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static readonly AsyncLocal<SaveTransaction?> s_active = new();
    private SaveTransaction? _previousActive;

    private readonly string _folder;
    private readonly string _pendingDir;
    private readonly List<Entry> _entries;
    private readonly Journal _journal;

    private SaveTransaction(string folder, string pendingDir, Journal journal)
    {
        _folder = folder;
        _pendingDir = pendingDir;
        _journal = journal;
        _entries = journal.Entries;
    }

    private sealed record Entry(string Path, string? Backup, string? OriginalVersion = null, string? WrittenVersion = null);

    /// <summary>
    /// What the journal remembers. <see cref="Known"/> lists the files that existed under the project (with the
    /// extensions in <see cref="Extensions"/>) before the save, so files the save created that nobody listed
    /// (a new namespace file, say) can be removed again on rollback.
    /// </summary>
    private sealed record Journal(List<Entry> Entries, List<string> Known, List<string> Extensions, bool DeclaredTargetsOnly = false, int Version = 1);

    /// <summary>Files this transaction protects, as absolute paths.</summary>
    public IReadOnlyList<string> Files => _entries.Select(e => Abs(e.Path)).ToList();

    public static string RecoveryRoot(string folder) => Path.Combine(folder, ".toucan", "recovery");

    /// <summary>Snapshots <paramref name="files"/> (existing ones are copied, missing ones are remembered as "new").</summary>
    public static SaveTransaction Begin(string folder, IEnumerable<string> files)
    {
        folder = Path.GetFullPath(folder);
        var root = RecoveryRoot(folder);
        var pending = Path.Combine(root, PendingName);

        // A journal left behind by an interrupted save that was never resolved must not be overwritten.
        if (Directory.Exists(pending))
            Directory.Move(pending, Path.Combine(root, $"interrupted-{DateTime.UtcNow:yyyyMMddHHmmssfff}"));

        Directory.CreateDirectory(pending);
        var entries = new List<Entry>();
        var index = 0;
        foreach (var file in files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            string? backup = null;
            if (File.Exists(file))
            {
                backup = $"{index++:D4}.bak";
                AtomicFile.WriteAllBytes(Path.Combine(pending, backup), File.ReadAllBytes(file));
            }
            entries.Add(new Entry(Rel(folder, file), backup));
        }

        var extensions = entries.Select(e => Path.GetExtension(e.Path)).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = ListProjectFiles(folder, extensions).Select(f => Rel(folder, f)).ToList();

        // The journal is written last: a crash while snapshotting leaves no journal, and nothing has been modified yet.
        var journal = new Journal(entries, known, extensions);
        AtomicFile.WriteAllText(Path.Combine(pending, JournalFile), JsonSerializer.Serialize(journal, s_json));
        var transaction = new SaveTransaction(folder, pending, journal) { _previousActive = s_active.Value };
        s_active.Value = transaction;
        return transaction;
    }

    /// <summary>Joins the calling save's journal or starts one. A journal from a previous run is never ownership evidence.</summary>
    public static SaveTransaction? BeginIfNeeded(string folder, IEnumerable<string> files)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var paths = files.Select(Path.GetFullPath).ToList();
        var active = s_active.Value;
        if (active != null && string.Equals(active._folder, Path.GetFullPath(folder), comparison) &&
            File.Exists(Path.Combine(active._pendingDir, JournalFile)))
        {
            if (paths.Any(p => !active.Files.Contains(p, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
                throw new InvalidOperationException("The active save journal does not protect every requested destination.");
            return null; // Only its owner commits or rolls back the surrounding save.
        }
        if (FindInterrupted(folder) != null)
            throw new IOException("Restore or discard the interrupted save before writing these resources.");
        return Begin(folder, paths);
    }

    private void ReleaseActive()
    {
        if (ReferenceEquals(s_active.Value, this)) s_active.Value = _previousActive;
    }

    /// <summary>Version-2 journal protecting exactly the declared targets. Missing versions require missing files.</summary>
    internal static SaveTransaction BeginDeclaredTargets(string folder, IReadOnlyDictionary<string, string?> expectedVersions)
    {
        folder = Path.GetFullPath(folder);
        var pending = LocalWorkspaceDocumentStore.SafePath(folder, ".toucan/recovery/pending");
        if (Directory.Exists(pending)) throw new IOException("Resolve the pending recovery journal before saving again.");
        var snapshots = new List<(string Path, byte[]? Bytes, string Version)>();
        foreach (var (relative, expected) in expectedVersions)
        {
            var path = LocalWorkspaceDocumentStore.SafePath(folder, relative);
            byte[]? bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { bytes = null; }
            catch (DirectoryNotFoundException) { bytes = null; }
            var version = bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
            if (version != expected) throw new WorkspaceSourceConflictException(relative, expected, version);
            snapshots.Add((relative, bytes, version ?? string.Empty));
        }
        Directory.CreateDirectory(pending);
        var entries = new List<Entry>();
        foreach (var (path, bytes, version) in snapshots)
        {
            var backup = bytes is null ? null : $"{entries.Count:D4}.bak";
            if (backup is not null) AtomicFile.WriteAllBytes(Path.Combine(pending, backup), bytes!);
            entries.Add(new(path, backup, version));
        }
        var journal = new Journal(entries, [], [], DeclaredTargetsOnly: true, Version: 2);
        AtomicFile.WriteAllText(Path.Combine(pending, JournalFile), JsonSerializer.Serialize(journal, s_json));
        return new(folder, pending, journal);
    }

    /// <summary>Durably records the intended post-write version before a target can be mutated. Null denotes deletion.</summary>
    internal void DeclareMutation(string relativePath, string? writtenVersion)
    {
        if (!_journal.DeclaredTargetsOnly) throw new InvalidOperationException("Mutation intents require a declared-target journal.");
        var index = _entries.FindIndex(e => e.Path == relativePath);
        if (index < 0) throw new InvalidOperationException("Mutation target was not declared.");
        _entries[index] = _entries[index] with { WrittenVersion = writtenVersion ?? string.Empty };
        AtomicFile.WriteAllText(Path.Combine(_pendingDir, JournalFile), JsonSerializer.Serialize(_journal, s_json));
    }

    /// <summary>The conditional store refused mutation; leave external content outside rollback ownership.</summary>
    internal void CancelMutation(string relativePath)
    {
        var index = _entries.FindIndex(e => e.Path == relativePath);
        if (!_journal.DeclaredTargetsOnly || index < 0) throw new InvalidOperationException("Mutation target was not declared.");
        _entries[index] = _entries[index] with { WrittenVersion = null };
        AtomicFile.WriteAllText(Path.Combine(_pendingDir, JournalFile), JsonSerializer.Serialize(_journal, s_json));
    }

    /// <summary>The save succeeded: keep the originals as the "previous" generation and drop the journal.</summary>
    public void Commit()
    {
        var root = RecoveryRoot(_folder);
        var previous = Path.Combine(root, PreviousName);
        try
        {
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            Directory.Move(_pendingDir, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The save itself succeeded; failing to rotate the backup must not turn it into a failure.
            TryDeleteDir(_pendingDir);
        }
        finally { ReleaseActive(); }
    }

    /// <summary>Puts every protected file back as it was before the save.</summary>
    public SaveRollbackResult Rollback()
    {
        if (!_journal.DeclaredTargetsOnly) return RollbackOwned();
        using var lease = AcquireDocumentLock();
        return RollbackOwned();
    }

    // The executor already holds the document lock through failure recovery.
    internal SaveRollbackResult RollbackOwned()
    {
        var restored = new List<string>();
        var failed = new List<(string, string)>();
        foreach (var entry in _entries)
        {
            var path = Abs(entry.Path);
            try
            {
                if (_journal.DeclaredTargetsOnly)
                {
                    path = LocalWorkspaceDocumentStore.SafePath(_folder, entry.Path);
                    if (entry.WrittenVersion is null) continue;
                    var current = LocalWorkspaceDocumentStore.Version(path) ?? string.Empty;
                    if (current == entry.OriginalVersion) continue;
                    if (current != entry.WrittenVersion)
                        throw new IOException("The target changed externally after the save started; recovery preserved its current content.");
                }
                if (entry.Backup != null)
                    AtomicFile.WriteAllBytes(path, File.ReadAllBytes(_journal.DeclaredTargetsOnly
                        ? LocalWorkspaceDocumentStore.SafePath(_folder, Path.GetRelativePath(_folder, Path.Combine(_pendingDir, entry.Backup)))
                        : Path.Combine(_pendingDir, entry.Backup)));
                else if (File.Exists(path))
                    File.Delete(path);
                restored.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add((path, ex.Message));
            }
        }

        // Files the save created that were not listed up front (same kind as the project's own files).
        var known = _journal.Known.ToHashSet(StringComparer.Ordinal);
        foreach (var created in _journal.DeclaredTargetsOnly ? [] : ListProjectFiles(_folder, _journal.Extensions))
        {
            if (known.Contains(Rel(_folder, created))) continue;
            try { File.Delete(created); restored.Add(created); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add((created, ex.Message)); }
        }

        if (failed.Count == 0) TryDeleteDir(_pendingDir);
        ReleaseActive();
        return new SaveRollbackResult(restored, failed);
    }

    /// <summary>Drops the journal without restoring anything (the user chose to keep what is on disk).</summary>
    public void Discard()
    {
        using var lease = _journal.DeclaredTargetsOnly ? AcquireDocumentLock() : null;
        TryDeleteDir(_pendingDir);
        ReleaseActive();
    }

    private FileStream AcquireDocumentLock() => new(LocalWorkspaceDocumentStore.SafePath(_folder,
        ".toucan/recovery/document-save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>A journal that was never committed or rolled back: the previous run died, or a restore failed, mid-save.</summary>
    public static SaveTransaction? FindInterrupted(string folder)
    {
        folder = Path.GetFullPath(folder);
        var pending = Path.Combine(RecoveryRoot(folder), PendingName);
        var journal = Path.Combine(pending, JournalFile);
        if (!File.Exists(journal)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journal), s_json);
            if (parsed?.Entries is null || parsed.Version is not (1 or 2) || parsed.Version == 2 && !parsed.DeclaredTargetsOnly) return null;
            if (parsed.DeclaredTargetsOnly && (parsed.Version != 2 || parsed.Entries.Any(e =>
                e.OriginalVersion is null || string.IsNullOrWhiteSpace(e.Path) || Path.IsPathRooted(e.Path) ||
                Path.GetRelativePath(folder, WorkspaceDocumentRouter.WithinRoot(folder, e.Path)).Replace(Path.DirectorySeparatorChar, '/') != e.Path ||
                e.Backup is not null && Path.GetFileName(e.Backup) != e.Backup))) return null;
            return new SaveTransaction(folder, pending, parsed with { Known = parsed.Known ?? [], Extensions = parsed.Extensions ?? [] });
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Content hashes used to notice that a file changed on disk behind Toucan's back.</summary>
    public static Dictionary<string, string> Fingerprint(IEnumerable<string> files)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            try
            {
                result[file] = File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result[file] = "unreadable";
            }
        }
        return result;
    }

    private static List<string> ListProjectFiles(string folder, List<string> extensions)
    {
        if (extensions.Count == 0 || !Directory.Exists(folder)) return [];
        try
        {
            return FileEnumerator.EnumerateFiles(folder, "*")
                .Where(f => !Path.GetFileName(f).StartsWith('.') && extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string Abs(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_folder, path);

    private static string Rel(string folder, string file)
    {
        var rel = Path.GetRelativePath(folder, file);
        return rel.StartsWith("..", StringComparison.Ordinal) ? file : rel;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
