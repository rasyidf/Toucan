using Toucan.Core.Options;

namespace Toucan.Core.Contracts;

/// <summary>Loads and saves <see cref="AiSettings"/>. Load returns a copy: change it and pass it to <see cref="Save"/>.</summary>
public interface IAiSettingsStore
{
    AiSettings Load();
    void Save(AiSettings settings);

    /// <summary>Raised after <see cref="Save"/>, so open views can follow the app-wide switch.</summary>
    event EventHandler? Changed;
}
