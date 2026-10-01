using Toucan.Core.Contracts;

namespace Toucan.Sample.Plugin;

/// <summary>Warns about translations that still carry a TODO or FIXME marker.</summary>
public sealed class TodoMarkerRule : IValidationRule
{
    private static readonly string[] s_markers = ["TODO", "FIXME"];

    public string Id => "sample.todo-marker";
    public string Name => "Unfinished translations (TODO/FIXME)";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var item in context.Items)
        {
            var marker = s_markers.FirstOrDefault(m => item.Value.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (marker is null) continue;

            yield return new ValidationResult(Id, DefaultSeverity, $"Contains a {marker} marker.", item.Namespace, item.Language);
        }
    }
}
