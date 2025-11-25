using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class OpenAITranslationProvider : ITranslationProvider
{
    public string Name => "OpenAI";

    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null)
    {
        var results = new List<PretranslationItemResult>();
        foreach (var item in items)
        {
            results.Add(new PretranslationItemResult
            {
                Namespace = item.Namespace ?? string.Empty,
                Language = item.Language,
                Provider = Name,
                Succeeded = true,
                TranslatedValue = $"[openai/{item.Language}] {item.Namespace}"
            });
        }

        return Task.FromResult<IEnumerable<PretranslationItemResult>>(results);
    }
}
