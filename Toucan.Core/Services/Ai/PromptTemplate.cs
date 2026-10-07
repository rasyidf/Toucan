using System.Text.RegularExpressions;

namespace Toucan.Core.Services.Ai;

/// <summary>
/// Fills in a prompt. Only variables the caller passes are touched, so placeholders written in the prompt as examples,
/// like <c>{{name}}</c>, stay as they are.
/// <list type="bullet">
/// <item><c>{{var}}</c> becomes the value (empty when the value is empty).</item>
/// <item><c>{{#var}}…{{/var}}</c> is kept only when the value is not empty; <c>{{^var}}…{{/var}}</c> only when it is.</item>
/// </list>
/// </summary>
public static partial class PromptTemplate
{
    [GeneratedRegex(@"\{\{([#^])([a-z0-9_]+)\}\}(.*?)\{\{/\2\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Section();

    [GeneratedRegex(@"\{\{([a-z0-9_]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();

    public static string Render(string template, IReadOnlyDictionary<string, string?> variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        string? Value(string name) => variables.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
        bool Known(string name) => variables.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

        var text = template;
        // Repeat so sections inside sections are resolved too.
        for (var pass = 0; pass < 4; pass++)
        {
            var next = Section().Replace(text, m =>
            {
                var name = m.Groups[2].Value;
                if (!Known(name)) return m.Value;
                var present = !string.IsNullOrEmpty(Value(name));
                var keep = m.Groups[1].Value == "#" ? present : !present;
                return keep ? m.Groups[3].Value : string.Empty;
            });
            if (next == text) break;
            text = next;
        }

        text = Variable().Replace(text, m => Known(m.Groups[1].Value) ? Value(m.Groups[1].Value) ?? string.Empty : m.Value);
        return text.Trim();
    }
}
