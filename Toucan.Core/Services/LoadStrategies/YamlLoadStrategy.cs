using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies
{
    public class YamlLoadStrategy : ILoadStrategy
    {
        public SaveStyles Style => SaveStyles.Yaml;

        public YamlLoadStrategy()
        {
        }

        public IEnumerable<TranslationItem> Load(string folder)
        {
            // Minimal placeholder: YAML support is not implemented yet
            return new List<TranslationItem>();
        }
    }
}
