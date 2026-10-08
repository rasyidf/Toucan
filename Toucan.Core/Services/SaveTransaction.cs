using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

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

    private sealed record Entry(string Path, string? Backup);

    /// <summary>
    /// What the journal remembers. <see cref="Known"/> lists the files that existed under the project (with the
    /// extensions in <see cref="Extensions"/>) before the save, so files the save created that nobody listed
    /// (a new namespace file, say) can be removed again on rollback.
    /// </summary>
    private sealed record Journal(List<Entry> Entries, List<string> Known, List<string> Extensions);

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
        return new SaveTransaction(folder, pending, journal);
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
    }

    /// <summary>Puts every protected file back as it was before the save.</summary>
    public SaveRollbackResult Rollback()
    {
        var restored = new List<string>();
        var failed = new List<(string, string)>();
        foreach (var entry in _entries)
        {
            var path = Abs(entry.Path);
            try
            {
                if (entry.Backup != null)
                    AtomicFile.WriteAllBytes(path, File.ReadAllBytes(Path.Combine(_pendingDir, entry.Backup)));
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
        foreach (var created in ListProjectFiles(_folder, _journal.Extensions))
        {
            if (known.Contains(Rel(_folder, created))) continue;
            try { File.Delete(created); restored.Add(created); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add((created, ex.Message)); }
        }

        if (failed.Count == 0) TryDeleteDir(_pendingDir);
        return new SaveRollbackResult(restored, failed);
    }

    /// <summary>Drops the journal without restoring anything (the user chose to keep what is on disk).</summary>
    public void Discard() => TryDeleteDir(_pendingDir);

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
            return parsed?.Entries == null ? null : new SaveTransaction(folder, pending, parsed with { Known = parsed.Known ?? [], Extensions = parsed.Extensions ?? [] });
        }
        catch (JsonException)
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
