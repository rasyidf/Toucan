using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface ITranslationStrategyFactory
    {
        ISaveStrategy GetSaveStrategy(SaveStyles style);
        ILoadStrategy GetLoadStrategy(SaveStyles style);
        ILoadStrategy GetManifestLoadStrategy();
    }
}
