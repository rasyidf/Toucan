using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

internal static class FormatJsTarget
{
    internal static void Validate(string content, ResourceDescriptor resource)
    {
        if (resource.Dialect != "formatjs-target") return;
        using var target = JsonDocument.Parse(content);
        if (target.RootElement.ValueKind != JsonValueKind.Object || target.RootElement.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String))
            throw new FormatException($"{resource.RelativePath}: FormatJS targets must map literal IDs to strings. Compiled AST targets are unsupported.");
    }
}
