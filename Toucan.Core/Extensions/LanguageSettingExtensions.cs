using Toucan.Core.Models;

namespace Toucan.Extensions;

public static class TranslationItemExtensions
{
    public static IEnumerable<TranslationItem> ForParse(this IEnumerable<TranslationItem> settings) =>
        settings.Where(o => !string.IsNullOrWhiteSpace(o.Namespace));

    public static IEnumerable<TranslationItem> NoEmpty(this IEnumerable<TranslationItem> settings) =>
        settings.ForParse().Where(o => !string.IsNullOrWhiteSpace(o.Value));

    public static IEnumerable<TranslationItem> ExcludeLanguage(this IEnumerable<TranslationItem> settings, string language) =>
        settings.Where(o => o.Language != language);

    public static IEnumerable<TranslationItem> OnlyLanguage(this IEnumerable<TranslationItem> settings, string language) =>
        settings.Where(o => o.Language == language);

    public static IEnumerable<string> ToNamespaces(this IEnumerable<TranslationItem> settings) =>
        settings.ForParse().Select(o => o.Namespace).Distinct();

    public static IEnumerable<string> ToNamespaces(this IEnumerable<TranslationItem> settings, string language) =>
        settings.NoEmpty().Where(o => o.Language == language).Select(o => o.Namespace).Distinct();

    public static IEnumerable<string> ToLanguages(this IEnumerable<TranslationItem> settings) =>
        settings.Select(o => o.Language).Distinct();

    public static Dictionary<string, IEnumerable<TranslationItem>> ToLanguageDictionary(this IEnumerable<TranslationItem> settings) =>
        settings.GroupBy(o => o.Language).ToDictionary(g => g.Key, g => g.ForParse());

    public static IEnumerable<NsTreeItem> ToNsTree(this IEnumerable<TranslationItem> settings)
    {
        var namespaces = settings.Select(o => o.Namespace.Split('.')[0]).Distinct().OrderBy(o => o).ToList();
        var root = new NsTreeItem { Name = "root", Namespace = "" };

        foreach (var ns in namespaces)
            settings.ProcessNs(root, ns);

        var nodes = root.Items.ToList();
        foreach (var node in nodes)
            node.Parent = null;
        root.Clear();

        return nodes;
    }

    public static void ProcessNs(this IEnumerable<TranslationItem> allTranslation, NsTreeItem node, string ns, int depth = 1, int customDepth = 0)
    {
        if (customDepth == 0) customDepth = 1;

        var thisNode = new NsTreeItem { Parent = node, Name = ns.Split('.').Last(), Namespace = ns, ImagePath = "Assets/Images/ns.png" };
        node.AddChild(thisNode);

        var namespaces = allTranslation
            .Where(o => o.Namespace.StartsWith(ns + "."))
            .Select(o => o.Namespace[(ns.Length + 1)..].Split('.')[0])
            .Distinct().OrderBy(o => o).ToList();

        if (namespaces.Count == 0)
        {
            thisNode.ImagePath = "Assets/Images/translation.png";
            thisNode.Settings = allTranslation.Where(o => o.Namespace == thisNode.Namespace);
            return;
        }

        var applicableSettings = allTranslation.Where(o => o.Namespace.StartsWith(ns + ".")).ToList();

        if (depth > customDepth)
        {
            thisNode.HeldSetttings = applicableSettings;
            return;
        }

        depth++;
        foreach (var nextNs in namespaces)
            applicableSettings.ProcessNs(thisNode, $"{ns}.{nextNs}", depth);

        thisNode.IsLoaded = true;
    }

    /// <summary>
    /// Flat tree for plain-text keys mode — no splitting at dot separator.
    /// Each unique namespace becomes a leaf node.
    /// </summary>
    public static IEnumerable<NsTreeItem> ToNsTreeFlat(this IEnumerable<TranslationItem> settings)
    {
        return settings.Select(o => o.Namespace).Distinct().OrderBy(o => o)
            .Select(ns => new NsTreeItem
            {
                Name = ns,
                Namespace = ns,
                ImagePath = "Assets/Images/translation.png",
                Settings = settings.Where(o => o.Namespace == ns)
            });
    }
}
