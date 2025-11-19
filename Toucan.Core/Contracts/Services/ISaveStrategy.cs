using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface ISaveStrategy
    {
        SaveStyles Style { get; }
        void Save(string path, SaveContext context);
    }
}
