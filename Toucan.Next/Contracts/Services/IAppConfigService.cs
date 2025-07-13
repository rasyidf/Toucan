using Toucan.Models;

namespace Toucan.Contracts.Services;
public interface IAppConfigService
{
    AppConfig Current { get; }
    void Save();
    void Reload();
}
