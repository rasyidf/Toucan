using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class LaravelPhpLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.LaravelPhp;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.php", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(file)!;
            var dirName = Path.GetFileName(dir);
            var fileBase = Path.GetFileNameWithoutExtension(file);

            // Language is the parent directory name (e.g., "en", "fr", "pt-BR")
            var lang = dirName;

            var lines = File.ReadAllLines(file);
            var nsStack = new Stack<string>();
            nsStack.Push(fileBase); // file prefix is first namespace segment

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("<?") || line.StartsWith("//")
                    || line == "return [" || line == "];") continue;

                // ponytail: naive bracket tracking for nested arrays
                if (line.EndsWith("["))
                {
                    // e.g. 'auth' => [
                    var key = ExtractKey(line);
                    if (key != null) nsStack.Push(key);
                    continue;
                }
                if (line == "]," || line == "]")
                {
                    if (nsStack.Count > 1) nsStack.Pop();
                    continue;
                }

                // 'key' => 'value',
                var (k, v) = ExtractKeyValue(line);
                if (k == null) continue;

                var ns = string.Join(".", nsStack.Reverse()) + "." + k;
                items.Add(new TranslationItem { Language = lang, Namespace = ns, Value = v });
            }
        }
        return items;
    }

    private static string? ExtractKey(string line)
    {
        // 'key' => [
        var start = line.IndexOf('\'');
        if (start < 0) return null;
        var end = line.IndexOf('\'', start + 1);
        if (end < 0) return null;
        return line[(start + 1)..end];
    }

    private static (string? key, string value) ExtractKeyValue(string line)
    {
        // 'key' => 'value',
        var arrow = line.IndexOf("=>");
        if (arrow < 0) return (null, "");

        var left = line[..arrow];
        var right = line[(arrow + 2)..];

        var key = ExtractQuoted(left);
        if (key == null) return (null, "");

        var val = ExtractQuoted(right) ?? "";
        return (key, val.Replace("\\'", "'"));
    }

    private static string? ExtractQuoted(string s)
    {
        var start = s.IndexOf('\'');
        if (start < 0) return null;
        // find closing quote, skipping escaped ones
        for (int i = start + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '\'') return s[(start + 1)..i];
        }
        return null;
    }
}
