using Toucan.Core.Contracts;
using Toucan.Core.Services;

namespace Toucan.Core.Services.Validation;

public sealed class MessageSyntaxRule : IValidationRule
{
    public string Id => "message-syntax";
    public string Name => "ICU message syntax";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var items = context.Items.ToList();
        var sources = items.Where(i => i.Language == context.PrimaryLanguage).GroupBy(i => i.Namespace)
            .ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var item in items.Where(i => !string.IsNullOrEmpty(i.Value)))
        {
            sources.TryGetValue(item.Namespace, out var source);
            if (!MessageSyntaxService.IsIcu(item.Value) && (source is null || !MessageSyntaxService.IsIcu(source))) continue;
            var message = MessageSyntaxService.Parse(item.Value);
            if (message.Error is { } error) { yield return new(Id, DefaultSeverity, error, item.Namespace, item.Language); continue; }
            if (item.Language == context.PrimaryLanguage || source is null) continue;
            var original = MessageSyntaxService.Parse(source);
            if (original.Error is not null) continue;
            var expected = original.Arguments.Select(a => (a.Name, a.Type)).ToHashSet();
            var actual = message.Arguments.Select(a => (a.Name, a.Type)).ToHashSet();
            foreach (var argument in expected.Except(actual))
                yield return new(Id, DefaultSeverity, $"Missing or changed message argument '{argument.Name}' ({argument.Type}).", item.Namespace, item.Language);
            foreach (var argument in actual.Except(expected))
                yield return new(Id, DefaultSeverity, $"Unexpected message argument '{argument.Name}' ({argument.Type}).", item.Namespace, item.Language);
            foreach (var argument in original.Arguments.Where(a => a.Type == "select"))
            {
                var target = message.Arguments.Where(a => a.Name == argument.Name && a.Type == "select").ToList();
                foreach (var selector in argument.Selectors.Where(s => !target.Any(t => t.Selectors.Contains(s))))
                    yield return new(Id, DefaultSeverity, $"Missing select branch '{selector}' for '{argument.Name}'.", item.Namespace, item.Language);
            }
        }
    }
}
