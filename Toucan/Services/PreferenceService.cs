using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Services;

internal class PreferenceService : IPreferenceService
{
    public AppOptions Load()
    {
        return AppOptions.LoadFromDisk();
    }

    public void Save(AppOptions options)
    {
        options.ToDisk();
    }
}
