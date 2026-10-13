using System.Windows.Input;
using FluentAvalonia.UI.Controls;
using Toucan.Core.Commands;
using Toucan.Plugins;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Commands plugins offer for the selected translation key: editor actions they registered with the desktop contract, and
/// plugin commands that asked for the key context menu. The key's name is the command's parameter.
/// </summary>
internal static class KeyActions
{
    public static IReadOnlyList<KeyActionItem> Collect(ICommandRegistry registry, DesktopContributions contributions)
    {
        var items = new List<KeyActionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, action) in contributions.KeyActions)
        {
            if (!registry.GetState(action.CommandId).IsVisible || !seen.Add(action.CommandId)) continue;
            items.Add(new KeyActionItem(LocalizedText.Pick(action.LocalizedTitles, action.Title), Icon(action.Icon), RegistryCommand.For(registry, action.CommandId),
                action.CommandId, action.Order));
        }

        foreach (var command in registry.Commands.Where(c => c.PluginId is not null
                                                              && c.Definition.Placements.HasFlag(CommandPlacement.ContextMenu)
                                                              && string.Equals(c.Definition.ContextMenuTarget, "key", StringComparison.OrdinalIgnoreCase)))
        {
            if (!registry.GetState(command.Id).IsVisible || !seen.Add(command.Id)) continue;
            items.Add(new KeyActionItem(registry.GetTitle(command.Id), Icon(command.Definition.Icon), RegistryCommand.For(registry, command.Id), command.Id, 100));
        }

        return [.. items.OrderBy(i => i.Order).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static FASymbol? Icon(string? name) => name is not null && Enum.TryParse<FASymbol>(name, ignoreCase: true, out var icon) ? icon : null;
}
