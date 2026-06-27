using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Avalonia.Services;

public class PreferenceService : IPreferenceService
{
    public AppOptions Load() => AppOptions.LoadFromDisk();
    public void Save(AppOptions options) => options.ToDisk();
}
