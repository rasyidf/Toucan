using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Services;

/// <inheritdoc />
public sealed class RecoveryDraftService(string? folder = null, ILogger<RecoveryDraftService>? logger = null) : IRecoveryDraftService
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "recovery");

    private readonly string _folder = folder ?? DefaultFolder;

    /// <inheritdoc />
    public void Write(string projectPath, RecoveryDraft draft)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var stamped = draft with { ProjectPath = Path.GetFullPath(projectPath), SavedAtUtc = DateTime.UtcNow };
            AtomicFile.WriteAllText(PathFor(projectPath), JsonSerializer.Serialize(stamped, s_json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A draft is a safety net; failing to write one must never interrupt editing.
            logger?.LogWarning(ex, "Could not write recovery draft for {Project}", projectPath);
        }
    }

    /// <inheritdoc />
    public RecoveryDraft? TryRead(string projectPath)
    {
        var file = PathFor(projectPath);
        if (!File.Exists(file)) return null;
        try
        {
            var draft = JsonSerializer.Deserialize<RecoveryDraft>(File.ReadAllText(file), s_json);
            return draft is { Entries: not null } && draft.ChangeCount > 0 ? draft : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(ex, "Could not read recovery draft for {Project}", projectPath);
            return null;
        }
    }

    /// <inheritdoc />
    public void Delete(string projectPath)
    {
        try
        {
            var file = PathFor(projectPath);
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not delete recovery draft for {Project}", projectPath);
        }
    }

    private string PathFor(string projectPath)
    {
        var key = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(_folder, hash + ".json");
    }
}
