using System.Collections.Generic;
using System.Threading.Tasks;
using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface IBulkActionService
    {
        Task PreTranslateAsync(IEnumerable<TranslationItem> items);

        string GenerateStatistics(IEnumerable<TranslationItem> items);
    }
}