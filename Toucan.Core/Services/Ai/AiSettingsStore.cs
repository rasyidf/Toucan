using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Core.Services.Ai;

/// <summary>
/// <see cref="AiSettings"/> in <c>Documents/Toucan/ai.json</c>. When the file does not exist yet, <paramref name="seed"/>
/// builds the first settings (the migration from the old Claude, OpenAI and Gemini provider entries) and they are saved,
/// so the migration runs once. The file is re-read when another process changes it.
/// </summary>
public sealed class AiSettingsStore(string file, Func<AiSettings>? seed = null) : IAiSettingsStore
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly Lock _lock = new();
    private AiSettings? _cached;
    private DateTime _stamp;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "ai.json");

    public event EventHandler? Changed;

    public string FilePath => file;

    public AiSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(file))
            {
                // Seed once per process, even when the file cannot be written (read-only Documents).
                if (_cached == null)
                {
                    _cached = Seed();
                    TryWrite(_cached);
                }
                return _cached.Clone();
            }

            var stamp = File.GetLastWriteTimeUtc(file);
            if (_cached == null || stamp != _stamp)
            {
                try { _cached = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(file), s_json) ?? new AiSettings(); }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { _cached = new AiSettings(); }
                _stamp = stamp;
            }
            return _cached.Clone();
        }
    }

    public void Save(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            Write(settings);
            _cached = settings.Clone();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The first settings. A migration that cannot read or rewrite the old files must not stop the app from starting.</summary>
    private AiSettings Seed()
    {
        try { return seed?.Invoke() ?? new AiSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new AiSettings(); }
    }

    private void Write(AiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(settings, s_json));
        _stamp = File.GetLastWriteTimeUtc(file);
    }

    private void TryWrite(AiSettings settings)
    {
        try { Write(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* read-only Documents: keep the settings in memory */ }
    }
}
