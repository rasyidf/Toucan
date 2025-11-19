using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services
{
    public class BulkActionService : IBulkActionService
    {
        public async Task PreTranslateAsync(IEnumerable<TranslationItem> items)
        {
            // Simple stub: set empty values to a placeholder or copy from another language (naive)
            await Task.Run(() =>
            {
                // pick first language with values as source
                var source = items.Where(i => !string.IsNullOrEmpty(i.Value)).FirstOrDefault();
                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.Value) && source != null)
                    {
                        item.Value = source.Value; // naive copy
                    }
                }
            }).ConfigureAwait(true);
        }

        public string GenerateStatistics(IEnumerable<TranslationItem> items)
        {
            var byLanguage = items.GroupBy(i => i.Language)
                .Select(g => new { Language = g.Key, Total = g.Count(), Missing = g.Count(i => string.IsNullOrEmpty(i.Value)) })
                .OrderBy(o => o.Language)
                .ToList();

            StringBuilder sb = new();
            sb.AppendLine("Translation statistics:");
            foreach (var l in byLanguage)
            {
                StringBuilder stringBuilder = sb.AppendLine($"{l.Language}: {l.Total - l.Missing}/{l.Total} ({(int)((double)(l.Total - l.Missing) / l.Total * 100)}% complete)");
            }

            return sb.ToString();
        }
    }
}