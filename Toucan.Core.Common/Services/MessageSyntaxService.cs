using System.Globalization;
using System.Text.RegularExpressions;

namespace Toucan.Core.Services;

/// <summary>Structural validation for the documented ICU MessageFormat 1 subset.</summary>
public static partial class MessageSyntaxService
{
    [GeneratedRegex(@"\{\s*[\w.]+\s*,\s*(plural|select|selectordinal)\s*,")]
    private static partial Regex ComplexArgument();
    public static bool IsIcu(string text) => ComplexArgument().IsMatch(text);


    public static ParsedMessage Parse(string text)
    {
        var parser = new Parser(text);
        try { parser.ReadMessage(false, 0); return new(parser.Arguments, null); }
        catch (FormatException ex) { return new(parser.Arguments, ex.Message); }
    }

    private sealed class Parser(string text)
    {
        private int _position;
        public List<MessageArgument> Arguments { get; } = [];
        private void Fail(string reason) => throw new FormatException($"{reason} at character {_position + 1}.");
        private void Space() { while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++; }
        private void Expect(char c) { Space(); if (_position >= text.Length || text[_position] != c) Fail($"Expected '{c}'"); _position++; }
        private string Token()
        {
            Space(); var start = _position;
            while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not (',' or '{' or '}')) _position++;
            if (start == _position) Fail("Expected argument or selector");
            return text[start.._position];
        }
        private void Quote()
        {
            _position++;
            if (_position < text.Length && text[_position] == '\'') { _position++; return; }
            if (_position >= text.Length || text[_position] is not ('{' or '}' or '#')) return;
            while (_position < text.Length)
            {
                if (text[_position++] != '\'') continue;
                if (_position < text.Length && text[_position] == '\'') { _position++; continue; }
                return;
            }
        }
        public void ReadMessage(bool nested, int depth)
        {
            if (depth > 64) Fail("Message nesting exceeds 64 levels");
            while (_position < text.Length)
            {
                switch (text[_position])
                {
                    case '\'': Quote(); break;
                    case '{': ReadArgument(depth + 1); break;
                    case '}': if (!nested) Fail("Unexpected closing brace"); _position++; return;
                    default: _position++; break;
                }
            }
            if (nested) Fail("Unclosed message branch");
        }
        private void ReadArgument(int depth)
        {
            Expect('{'); var name = Token();
            if (!name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.')) Fail("Unsupported argument name");
            Space();
            if (_position < text.Length && text[_position] == '}') { _position++; Arguments.Add(new(name, "simple", new HashSet<string>())); return; }
            Expect(','); var type = Token(); Space();
            if (type is not ("plural" or "select" or "selectordinal"))
            {
                if (type is not ("number" or "date" or "time")) Fail($"Unsupported argument type '{type}'");
                if (_position < text.Length && text[_position] == ',')
                {
                    _position++;
                    while (_position < text.Length && text[_position] != '}')
                    { if (text[_position] == '{') Fail("Nested formatter styles are unsupported"); _position++; }
                }
                Expect('}'); Arguments.Add(new(name, type, new HashSet<string>())); return;
            }
            Expect(','); Space();
            if (type != "select" && text.AsSpan(_position).StartsWith("offset:", StringComparison.Ordinal))
            {
                _position += 7; Space(); var start = _position;
                while (_position < text.Length && char.IsAsciiDigit(text[_position])) _position++;
                if (start == _position) Fail("Expected nonnegative plural offset");
            }
            var selectors = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                Space(); if (_position >= text.Length) Fail("Unclosed argument");
                if (text[_position] == '}') { _position++; break; }
                var selector = Token();
                if (!selectors.Add(selector)) Fail($"Duplicate selector '{selector}'");
                if (type != "select" && selector is not ("zero" or "one" or "two" or "few" or "many" or "other") &&
                    !(selector.StartsWith('=') && decimal.TryParse(selector.AsSpan(1), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)))
                    Fail($"Invalid plural selector '{selector}'");
                Expect('{'); ReadMessage(true, depth);
            }
            if (!selectors.Contains("other")) Fail("Missing required 'other' branch");
            Arguments.Add(new(name, type, selectors));
        }
    }
}

public sealed record MessageArgument(string Name, string Type, IReadOnlySet<string> Selectors);
public sealed record ParsedMessage(IReadOnlyList<MessageArgument> Arguments, string? Error);
