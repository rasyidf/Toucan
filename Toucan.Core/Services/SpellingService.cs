using Toucan.Core.Models;
using WeCantSpell.Hunspell;

namespace Toucan.Core.Services;

public sealed class SpellingService(DictionaryRegistry registry)
{
    public Task<SpellingResult> CheckAsync(string text, string locale, SpellingSettings settings,
        IReadOnlySet<string>? ignored = null, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.Enabled) return new SpellingResult(text, locale, null, "Spelling is disabled for this project.", [], false);
        try
        {
            var normalized = DictionaryRegistry.NormalizeLocale(locale);
            var (pack, status) = registry.Resolve(normalized, settings);
            if (pack == null) return new(text, locale, null, status, [], false);
            var dictionary = await registry.LoadAsync(pack, cancellationToken).ConfigureAwait(false);
            var allowed = new HashSet<string>(registry.UserWords(normalized), StringComparer.Ordinal);
            allowed.UnionWith(settings.Words.GetValueOrDefault(normalized) ?? []);
            if (ignored != null) allowed.UnionWith(ignored);
            var issues = new List<SpellingIssue>();
            var options = new QueryOptions { MaxSuggestions = 6, MaxWordLen = 128, TimeLimitSuggestGlobal = TimeSpan.FromMilliseconds(100), TimeLimitCompoundCheck = TimeSpan.FromMilliseconds(30) };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            foreach (var word in SpellingText.Words(text))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (allowed.Contains(word.Word) || dictionary.Check(word.Word, options, timeout.Token)) continue;
                issues.Add(new(word.Offset, word.Length, word.Word, dictionary.Suggest(word.Word, options, timeout.Token).Take(6).ToList()));
                if (issues.Count == 128) break;
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (!registry.Packs.Any(p => p.Id == pack.Id)) return new(text, locale, null, "Dictionary removed; select another pack.", [], false);
            return new(text, locale, pack.Id, issues.Count >= 128 ? status + " · finding limit reached; remaining text may be unchecked." : status, issues, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(text, locale, null, "Spelling time limit reached; editing and saving remain available.", [], false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or System.Xml.XmlException)
        { return new(text, locale, null, "Spelling unavailable: " + ex.Message, [], false); }
    }, cancellationToken);
}
