using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IParser
{
    IParser SetLanguage(string language);
    IAsyncEnumerable<TranslationItem> Parse(string content);
}


