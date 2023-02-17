using OPEdit.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPEdit.Core.Models;

public class LanguageGroup
{
    public string Namespace { get; private set; }
    public IEnumerable<LanguageSetting> Translations { get; private set; }
    private IEnumerable<string> languages { get; set; }

    public LanguageGroup(string ns, IEnumerable<string> languages)
    {
        Namespace = ns;
        this.languages = languages;
    }
    public void LoadSettings(IEnumerable<LanguageSetting> settings)
    {
        Translations = settings.ForParse().Distinct().OrderBy(o => o.Language).ToList();
    }
}
