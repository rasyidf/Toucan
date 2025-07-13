using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Toucan.Core.Models;

namespace Toucan.Extensions;

public static class TranslationItemExtensions
{

    public static IEnumerable<TranslationItem> ForParse(this IEnumerable<TranslationItem> settings)
    {
        return settings.Where(o => !string.IsNullOrWhiteSpace(o.Namespace));
    }
    public static IEnumerable<TranslationItem> NoEmpty(this IEnumerable<TranslationItem> settings)
    {
        return settings.ForParse().Where(o => !string.IsNullOrWhiteSpace(o.Value));
    }
    public static IEnumerable<TranslationItem> ExcludeLanguage(this IEnumerable<TranslationItem> settings, string language)
    {
        return settings.Where(o => o.Language != language);
    }
    public static IEnumerable<TranslationItem> OnlyLanguage(this IEnumerable<TranslationItem> settings, string language)
    {
        return settings.Where(o => o.Language == language);
    }

    public static IEnumerable<string> ToNamespaces(this IEnumerable<TranslationItem> settings)
    {
        return settings.ForParse().Select(o => o.Namespace).Distinct();
    }
    public static IEnumerable<string> ToNamespaces(this IEnumerable<TranslationItem> settings, string language)
    {
        return settings.NoEmpty().Where(o => o.Language == language).Select(o => o.Namespace).Distinct();
    }

    public static IEnumerable<string> ToLanguages(this IEnumerable<TranslationItem> settings)
    {
        return settings.Select(o => o.Language).Distinct();
    }

    public static Dictionary<string, IEnumerable<TranslationItem>> ToLanguageDictionary(this IEnumerable<TranslationItem> settings)
    {
        var seperatedByLanguage = settings.GroupBy(o => o.Language).Select(o => new { Language = o.Key, Settings = o.Select(p => p) });
        var dictionary = new Dictionary<string, IEnumerable<TranslationItem>>();

        foreach (var matches in seperatedByLanguage)
        {
            dictionary.Add(matches.Language, matches.Settings.ForParse());
        }
        return dictionary;
    }

    public static IEnumerable<NsTreeItem> ToNsTree(this IEnumerable<TranslationItem> settings)
    {
        var namespaces = settings.Select(o => o.Namespace.Split('.')[0]).Distinct().OrderBy(o => o).ToList();
        var root = new NsTreeItem() { Name = "root" };

        foreach (var ns in namespaces)
        {
            settings.ProcessNs(root, ns);
        }

        var nodes = new List<NsTreeItem>();
        foreach (NsTreeItem node in root.Items)
        {
            nodes.Add(node);
            node.Parent = null;
        }
        root.Clear();


        return nodes;
    }

    public static void ProcessNs(this IEnumerable<TranslationItem> AllTranslation,  NsTreeItem node, string ns, int depth = 1, int customDepth = 0)
    {
        if (customDepth == 0)
            customDepth = 1;

        var thisNode = new NsTreeItem() { Parent = node, Name = (ns.Split('.').Last()), Namespace = ns, ImagePath = "Assets/Images/ns.png" };

        if (node == null)
            node = thisNode;
        else
        {
            node.AddChild(thisNode);
        }

        var namespaces = AllTranslation.Where(o => o.Namespace.StartsWith(ns + ".")).Select(o => o.Namespace.Substring(ns.Length + 1).Split('.')[0]).Distinct().OrderBy(o => o).ToList();

        if (!namespaces.Any())
        {
            thisNode.ImagePath = "Assets/Images/translation.png";
            thisNode.Settings = AllTranslation.Where(o => o.Namespace == thisNode.Namespace);
            return;
        }

        var applicableSettings = AllTranslation.Where(o => o.Namespace.StartsWith(ns + ".")).ToList();
        
        if (depth > customDepth)
        {
            thisNode.HeldSetttings = applicableSettings;
            return;
        }

        depth++;
        foreach (var nextNs in namespaces)
        {
            applicableSettings.ProcessNs(thisNode, $"{ns}.{nextNs}", depth);
        }

        thisNode.IsLoaded = true;
    }

}
