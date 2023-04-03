using OPEdit.Core.Models;
using System.Collections.Generic;

namespace OPEdit.Core.Contracts;

public interface IParser
{
    IParser SetLanguage(string language);
    IAsyncEnumerable<TranslationItem> Parse(string content);
}


