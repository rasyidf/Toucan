using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class MockTranslationProvider : ITranslationProvider
{
    public string Name => "Mock";

    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null)
    {
        var results = new List<PretranslationItemResult>();

        foreach (var item in items)
        {
            var r = new PretranslationItemResult { Namespace = item.Namespace ?? string.Empty, Language = item.Language, Provider = Name };

            if (!string.IsNullOrEmpty(item.Value) && (options == null || !options.Overwrite))
            {
                r.Succeeded = false;
                r.ErrorMessage = "Skipped — existing translation present";
            }
            else
            {
                // Simple mock: echo back a synthetic translation
                r.TranslatedValue = $"[mock/{item.Language}] {item.Namespace}";
                r.Succeeded = true;
            }

            results.Add(r);
        }

        return Task.FromResult<IEnumerable<PretranslationItemResult>>(results);
    }
}
