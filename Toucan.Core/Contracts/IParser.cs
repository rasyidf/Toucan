using Toucan.Core.Models;
using System.Collections.Generic;

namespace Toucan.Core.Contracts;

public interface IParser
{
    IParser SetLanguage(string language);
    IAsyncEnumerable<TranslationItem> Parse(string content);
}


