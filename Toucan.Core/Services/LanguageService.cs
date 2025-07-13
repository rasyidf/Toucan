using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Toucan.Core
{
    internal class LanguageService
    {
        public static readonly LanguageService Instance = new ();

        private readonly List<LanguageModel> languages;

        public bool LanguageExists(string language)
        {
            return languages.Exists((l)=> l.Culture.Name  == language || l.Language == language);
        }
    }

    public class LanguageModel : IDataErrorInfo
    {
        public CultureInfo Culture { get; set; }
        public string Language { get; set; }

        public string this[string columnName]
        {
            get
            {
                if (columnName == "Language")
                {
                    if (LanguageService.Instance.LanguageExists(Language))
                    {
                        return "Language already exists";
                    }
                }
                return null;
            }
        }

        public string Error
        {
            get;
        }
        public override string ToString()
        {
            return Culture?.NativeName ?? Language ?? base.ToString();
        }


    }
}
