using Toucan.Core.Plugins;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Startup nudge for plugins that are installed but not loaded because they are not trusted (or changed since).
/// It never trusts anything itself: the only way to trust is the explicit confirmation on the Plugins page.
/// </summary>
public static class PluginPrompt
{
    /// <summary>Plugins worth asking about: needs trust, files readable, and the user hasn't said "don't ask again" for this content.</summary>
    public static IReadOnlyList<PluginLoadResult> Pending(IPluginCatalog catalog, IPluginPolicyStore policy) =>
        [.. catalog.Plugins.Where(p => p.Status == PluginStatus.NeedsTrust
                                       && p.ContentHash is not null
                                       && !policy.IsPromptDismissed(p.DisplayId, p.ContentHash))];

    /// <returns>True if the user chose to review plugins.</returns>
    public static async Task<bool> RunAsync(IPluginCatalog catalog, IPluginPolicyStore policy, IAsyncMessageService messages, Action openPlugins)
    {
        var pending = Pending(catalog, policy);
        if (pending.Count == 0) return false;

        var lines = pending.Select(p =>
        {
            var name = p.Manifest?.Name is { Length: > 0 } n ? n : p.DisplayId;
            var version = p.Manifest?.Version is { Length: > 0 } v ? $" {v}" : string.Empty;
            return $"• {name}{version}" + (p.Trust == PluginTrustState.Changed ? " (changed since you trusted it)" : string.Empty);
        });
        var noun = pending.Count == 1 ? "plugin is" : "plugins are";
        var message = $"{pending.Count} {noun} installed but not loaded, because you have not trusted {(pending.Count == 1 ? "it" : "them")} yet:\n\n" +
                      string.Join("\n", lines) +
                      "\n\nPlugins run code with your permissions. Review them before trusting.";

        switch (await messages.ChooseAsync(message, "Plugins found", "Review plugins", "Don't ask again", "Later"))
        {
            case ChoiceResult.Primary:
                openPlugins();
                return true;
            case ChoiceResult.Secondary:
                foreach (var p in pending) policy.DismissPrompt(p.DisplayId, p.ContentHash!);
                return false;
            default:
                return false;
        }
    }
}
