using Toucan.Core.Contracts;
using Toucan.Core.Services;

namespace Toucan.Core.Services.Frameworks;

/// <summary>Small lexical parser for static public i18n syntax. Unsupported expressions are findings, never guessed keys.</summary>
public sealed class FrameworkSourceScanner(string id) : ISourceScanner
{
    public string Id => id;
    public SourceFileResult Extract(SourceScanFile file) => ExtractCore(file, 0);
    private SourceFileResult ExtractCore(SourceScanFile file, int embeddingDepth)
    {
        if (embeddingDepth >= 8) return new([], [new("source.parser-limit", "Embedded source depth limit exceeded.", file.File)]);
        var (tokens, diagnostics) = SourceSyntax.Tokenize(file, id == "rails");
        var references = new List<SourceReference>();
        var budget = new ParseBudget();
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? boundPackage = null; var boundPrefix = "";
        var bindings = new List<(string Package, string Prefix)>();
        for (var i = 0; i + 2 < tokens.Count; i++)
            if (id == "i18next" && tokens[i].Text == "useTranslation" && tokens[i + 1].Text == "(" && tokens[i + 2] is { Literal: true, Dynamic: false } binding)
                bindings.Add((binding.Text, Option(tokens, i + 3, End(tokens, i + 1, budget), "keyPrefix") ?? ""));
        if (id == "transloco")
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "provideTranslocoScope" && i + 2 < tokens.Count)
                {
                    if (tokens[i + 2].Literal && tokens[i + 2].Text.All(char.IsAsciiLetterOrDigit)) scopes[tokens[i + 2].Text] = tokens[i + 2].Text;
                    else diagnostics.Add(new("source.binding-unresolved", "Object, aliased, dynamic or case-transformed Transloco scopes require explicit source resolution.", file.File));
                }
                if (tokens[i].Literal && i > 2 && tokens[i - 1].Text == "=" && tokens[i - 2].Text == "transloco" && tokens[i].Text.Contains("let ", StringComparison.Ordinal))
                {
                    var body = SourceSyntax.Tokenize(new(file.File, tokens[i].Text, file.Options, file.CancellationToken)).Tokens;
                    string? prefix = null;
                    for (var n = 0; n + 2 < body.Count; n++)
                    {
                        if (body[n + 1].Text != ":") continue;
                        if (body[n].Text is "read" or "prefix")
                        {
                            if (body[n + 2].Literal && !body[n + 2].Dynamic) prefix = body[n + 2].Text;
                            else diagnostics.Add(new("source.unresolved", "Dynamic Transloco prefix.", file.File));
                        }
                        if (body[n].Text == "scope" && body[n + 2].Literal) scopes[body[n + 2].Text] = body[n + 2].Text;
                    }
                    if (prefix != null) bindings.Add((file.Options.DefaultPackage, prefix));
                }
            }
        var bindingCount = bindings.Distinct().Count();
        if (id is "i18next" or "vue")
            for (var n = 0; n + 2 < tokens.Count; n++)
            {
                if (!tokens[n].Literal && tokens[n].Text is "useTranslation" or "useI18n" && tokens[n + 1].Text == "(" && tokens[n + 2].Text != ")" && !tokens[n + 2].Literal && id == "i18next")
                    diagnostics.Add(new("source.binding-unresolved", "Nonliteral translation bindings require explicit namespace resolution.", file.File));
                if (!tokens[n].Literal && tokens[n].Text == "t" && tokens[n + 1].Text is ":" or "as" && !tokens[n + 2].Literal)
                    diagnostics.Add(new("source.binding-unresolved", "Renamed translation functions are outside the supported binding subset.", file.File));
            }
        var unresolvedBinding = diagnostics.Any(d => d.Code == "source.binding-unresolved");
        if (bindingCount == 1) (boundPackage, boundPrefix) = bindings[0];
        if (bindingCount > 1) diagnostics.Add(new("source.binding-scope", "Multiple translation bindings require scope-aware resolution; unqualified calls remain unresolved.", file.File));

        void Unresolved(SourceToken token, string reason)
        {
            var location = SourceSyntax.Reference(file, token, "", null);
            diagnostics.Add(new("source.unresolved", reason, file.File, location.Line, location.Column));
        }
        void Add(SourceToken token, string? package = null, string? prefix = null, bool useBinding = true)
        {
            if (token.Dynamic || !token.Literal) { Unresolved(token, "Dynamic translation expression."); return; }
            var key = token.Text;
            if (unresolvedBinding && id is "i18next" or "transloco" && package == null && !key.Contains(':'))
            { Unresolved(token, "Translation package binding is unresolved."); return; }
            if (key.Length == 0) { Unresolved(token, "Empty translation key."); return; }
            if (id == "rails" && key.StartsWith('.'))
            {
                var path = file.File.Replace('\\', '/'); var views = path.IndexOf("views/", StringComparison.Ordinal);
                if (views < 0) { Unresolved(token, "Relative Rails key outside a views path."); return; }
                var relative = path[(views + 6)..];
                var extension = relative.IndexOf('.');
                relative = string.Join('.', (extension < 0 ? relative : relative[..extension]).Split('/').Select(p => p.TrimStart('_')));
                key = relative + key;
            }
            if (file.Options.ResolveNamespaces && id is "i18next" or "transloco")
            {
                var colon = key.IndexOf(':');
                if (colon > 0) { package = key[..colon]; key = key[(colon + 1)..]; }
            }
            if (!file.Options.ResolveNamespaces) package = file.Options.DefaultPackage;
            package ??= useBinding ? boundPackage ?? file.Options.DefaultPackage : file.Options.DefaultPackage;
            prefix ??= useBinding && boundPrefix.Length > 0 ? boundPrefix : file.Options.KeyPrefix;
            if (prefix.Length > 0) key = prefix + "." + key;
            if (id == "transloco" && file.Options.ResolveNamespaces)
                foreach (var scope in scopes)
                    if (key.StartsWith(scope.Key + ".", StringComparison.Ordinal)) { package = scope.Value; key = key[(scope.Key.Length + 1)..]; break; }
            if (useBinding && bindingCount > 1 && package == file.Options.DefaultPackage) { Unresolved(token, "Ambiguous translation binding."); return; }
            references.Add(SourceSyntax.Reference(file, token, key, package));
        }
        for (var i = 0; i < tokens.Count; i++)
        {
            if ((i & 255) == 0) file.CancellationToken.ThrowIfCancellationRequested();
            if (budget.Remaining <= 0 || references.Count >= 100_000) { diagnostics.Add(new("source.parser-limit", "Source parser work limit exceeded.", file.File)); break; }
            var token = tokens[i];
            if (token.Literal)
            {
                var directive = i > 1 && tokens[i - 1].Text == "=" &&
                    (i > 3 && (tokens[i - 3].Text == ":" || tokens[i - 2].Text == "]") || i > 4 && tokens[i - 4].Text == "v" && tokens[i - 3].Text == "-");
                if ((id == "vue" || id == "transloco") && directive)
                {
                    var inner = token.Text;
                    var extracted = ExtractCore(new(file.File, inner, file.Options with { DefaultPackage = boundPackage ?? file.Options.DefaultPackage }, file.CancellationToken), embeddingDepth + 1);
                    foreach (var reference in extracted.References)
                    {
                        var location = SourceSyntax.Reference(file, new("", token.Offset + 1 + reference.Offset), reference.Key, reference.Package);
                        references.Add(location);
                    }
                    foreach (var diagnostic in extracted.Diagnostics) diagnostics.Add(diagnostic with { Message = "Template expression: " + diagnostic.Message });
                    // Direct translation directives require a static literal expression.
                    var direct = id == "vue" && i > 3 && tokens[i - 2].Text == "t" || id == "transloco" && i > 3 && tokens[i - 3].Text == "transloco";
                    if (extracted.References.Count == 0 && direct)
                    {
                        var body = SourceSyntax.Tokenize(new(file.File, inner, file.Options, file.CancellationToken)).Tokens;
                        if (body.Count == 1 && body[0].Literal && !body[0].Dynamic) Add(body[0] with { Offset = token.Offset + 1 + body[0].Offset });
                        else Unresolved(token, "Dynamic or unsupported translation directive expression.");
                    }
                }
                if (id == "transloco" && i > 1 && tokens[i - 1].Text == "=" && tokens[i - 2].Text == "transloco" && !token.Text.Contains("let ", StringComparison.Ordinal)) Add(token, useBinding: false);
                continue;
            }
            if (id == "transloco" && i > 1 && token.Text == "transloco" && tokens[i - 1].Text == "|" && tokens[i - 2].Literal)
            { Add(tokens[i - 2], useBinding: false); continue; }
            if (id == "generic" && token.Text == "Localizer" && i + 2 < tokens.Count && tokens[i + 1].Text == "[" && tokens[i + 2].Literal) Add(tokens[i + 2]);
            if (token.Text == "<" && i + 1 < tokens.Count && (id == "formatjs" && tokens[i + 1].Text == "FormattedMessage" || id == "vue" && tokens[i + 1].Text == "i18n"))
            {
                var end = i + 2; while (end < tokens.Count && tokens[end].Text != ">") end++;
                var name = id == "formatjs" ? "id" : "keypath";
                var found = false;
                for (var a = i + 2; a + 2 < end; a++)
                    if (tokens[a].Text == name && tokens[a + 1].Text == "=")
                    {
                        var v = a + 2; if (tokens[v].Text == "{") v++;
                        if (v < end && tokens[v].Literal && (v + 1 == end || tokens[v + 1].Text is "}" or "/" || tokens[a + 2].Literal)) Add(tokens[v]);
                        else Unresolved(tokens[a], "Dynamic template message ID."); found = true; break;
                    }
                if (!found) Unresolved(token, "Template message has no static ID.");
                continue;
            }
            var call = id switch
            {
                "formatjs" => token.Text is "formatMessage" or "defineMessages" or "defineMessage",
                "transloco" => token.Text is "translate" or "selectTranslate" or "t",
                "rails" => token.Text is "t" or "translate",
                "vue" => token.Text is "t" or "$t" or "tc" or "$tc",
                _ => token.Text is "t" or "$t" or "GetString" or "NSLocalizedString" or "tr" or "_" or "__"
            };
            if (!call || i + 1 >= tokens.Count || i > 0 && tokens[i - 1].Text is "function" or "def") continue;
            if (tokens[i + 1].Text != "(")
            {
                if (tokens[i + 1].Text == "?") { Unresolved(token, "Optional translation calls require source-level resolution."); continue; }
                if (id == "rails" && tokens[i + 1].Literal) Add(tokens[i + 1]);
                else if (id == "rails" && tokens[i + 1].Text == ":" && i + 2 < tokens.Count) Add(tokens[i + 2] with { Literal = true });
                continue;
            }
            var endCall = End(tokens, i + 1, budget);
            if (endCall < 0) { Unresolved(token, "Unclosed translation call."); continue; }
            var first = i + 2;
            if (id == "formatjs")
            {
                var found = false; var depth = 0;
                if (Enumerable.Range(first, Math.Max(0, endCall - first - 2)).Any(a => tokens[a].Text == "." && tokens[a + 1].Text == "." && tokens[a + 2].Text == "."))
                    Unresolved(token, "Spread message descriptors require source-level resolution.");
                for (var a = first; a + 2 < endCall; a++)
                {
                    if (!tokens[a].Literal && tokens[a].Text == "{") depth++;
                    if (!tokens[a].Literal && tokens[a].Text == "}") depth--;
                    if (depth == (token.Text == "defineMessages" ? 2 : 1) && tokens[a].Text == "id" && tokens[a + 1].Text == ":")
                    {
                        if (tokens[a + 2].Literal && tokens[a + 3].Text is "," or "}") Add(tokens[a + 2]);
                        else Unresolved(tokens[a], "FormatJS descriptor has a dynamic ID."); found = true;
                    }
                }
                if (!found) Unresolved(token, "FormatJS descriptor requires an explicit static ID; generated ID hashing is not inferred.");
                i = endCall; continue;
            }
            if (HasDynamicOption(tokens, first, endCall, "ns")) { Unresolved(token, "Dynamic namespace option."); i = endCall; continue; }
            if (id == "rails" && HasDynamicOption(tokens, first, endCall, "scope")) { Unresolved(token, "Nonliteral Rails scope is unresolved."); continue; }
            var ns = Option(tokens, first, endCall, "ns");
            var scope = id == "rails" ? Option(tokens, first, endCall, "scope") : null;
            if (id == "rails" && first + 1 < endCall && tokens[first].Text == ":" && (first + 2 == endCall || tokens[first + 2].Text == ","))
            { Add(tokens[first + 1] with { Literal = true }, prefix: scope); continue; }
            if (first < endCall && tokens[first].Text == "[")
            {
                var endArray = End(tokens, first, budget);
                if (endArray < 0 || endArray > endCall) { Unresolved(token, "Unclosed fallback-key list."); continue; }
                for (var a = first + 1; a < endArray; a++)
                    if (tokens[a].Text != ",")
                    {
                        if (tokens[a].Literal && (a + 1 == endArray || tokens[a + 1].Text == ",")) Add(tokens[a], ns);
                        else { Unresolved(tokens[a], "Dynamic fallback-key list."); break; }
                    }
            }
            else if (first < endCall && tokens[first].Literal && (first + 1 == endCall || tokens[first + 1].Text == ",")) Add(tokens[first], ns, prefix: scope, useBinding: id != "transloco" || token.Text == "t");
            else Unresolved(token, "Translation key is not a static string.");
            // A dynamic namespace cannot safely become the default package.
            if (HasDynamicOption(tokens, first, endCall, "ns")) Unresolved(token, "Dynamic namespace option.");
        }
        return new(references.Distinct().ToList(), diagnostics);
    }
    private static string? Option(List<SourceToken> tokens, int start, int end, string key)
    {
        for (var i = start; i + 2 < end; i++)
            if (tokens[i].Text == key && tokens[i + 1].Text == ":" && tokens[i + 2] is { Literal: true, Dynamic: false } value && tokens[i + 3].Text is "," or "}" or ")" or ";") return value.Text;
        return null;
    }
    private static bool HasDynamicOption(List<SourceToken> tokens, int start, int end, string key)
    {
        for (var i = start; i + 2 < end; i++) if (tokens[i].Text == key && tokens[i + 1].Text == ":" && (!tokens[i + 2].Literal || tokens[i + 2].Dynamic)) return true;
        return false;
    }
    private sealed class ParseBudget { public int Remaining = 2_000_000; }
    private static int End(List<SourceToken> tokens, int start, ParseBudget budget)
    {
        var open = tokens[start].Text; var close = open switch { "(" => ")", "[" => "]", "{" => "}", _ => "" };
        var depth = 0;
        for (var i = start; i < tokens.Count && i - start < 50_000; i++)
        {
            if (--budget.Remaining <= 0) return -1;
            if (tokens[i].Literal) continue;
            if (tokens[i].Text == open) depth++;
            if (tokens[i].Text == close && --depth == 0) return i;
        }
        return -1;
    }
}
