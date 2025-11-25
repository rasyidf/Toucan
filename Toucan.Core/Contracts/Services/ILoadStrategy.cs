using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface ILoadStrategy
    {
        SaveStyles Style { get; }
        IEnumerable<TranslationItem> Load(string folder);
    }
}
