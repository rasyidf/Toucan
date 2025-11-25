using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface IProjectModeResolver
    {
        ProjectTypeVariant Resolve(string path);
    }
}
