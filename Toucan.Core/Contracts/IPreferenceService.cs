using Toucan.Core.Options;

namespace Toucan.Core.Contracts;

public interface IPreferenceService
{
    AppOptions Load();
    void Save(AppOptions options);
}
