using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPEdit.Core.Models;

namespace OPEdit.Extensions;

public static class LanguageSettingExtensions
{

    public static IEnumerable<LanguageSetting> ForParse(this IEnumerable<LanguageSetting> settings)
    {
        return settings.Where(o => !string.IsNullOrWhiteSpace(o.Namespace));
    }
    public static IEnumerable<LanguageSetting> NoEmpty(this IEnumerable<LanguageSetting> settings)
    {
        return settings.ForParse().Where(o => !string.IsNullOrWhiteSpace(o.Value));
    }
    public static IEnumerable<LanguageSetting> ExcludeLanguage(this IEnumerable<LanguageSetting> settings, string language)
    {
        return settings.Where(o => o.Language != language);
    }
    public static IEnumerable<LanguageSetting> OnlyLanguage(this IEnumerable<LanguageSetting> settings, string language)
    {
        return settings.Where(o => o.Language == language);
    }

    public static IEnumerable<string> ToNamespaces(this IEnumerable<LanguageSetting> settings)
    {
        return settings.ForParse().Select(o => o.Namespace).Distinct();
    }
    public static IEnumerable<string> ToNamespaces(this IEnumerable<LanguageSetting> settings, string language)
    {
        return settings.NoEmpty().Where(o => o.Language == language).Select(o => o.Namespace).Distinct();
    }

    public static IEnumerable<string> ToLanguages(this IEnumerable<LanguageSetting> settings)
    {
        return settings.Select(o => o.Language).Distinct();
    }

    public static Dictionary<string, IEnumerable<LanguageSetting>> ToLanguageDictionary(this IEnumerable<LanguageSetting> settings)
    {
        var seperatedByLanguage = settings.GroupBy(o => o.Language).Select(o => new { Language = o.Key, Settings = o.Select(p => p) });
        var dictionary = new Dictionary<string, IEnumerable<LanguageSetting>>();

        foreach (var matches in seperatedByLanguage)
        {
            dictionary.Add(matches.Language, matches.Settings.ForParse());
        }
        return dictionary;
    }

    public static IEnumerable<NsTreeItem> ToNsTree(this IEnumerable<LanguageSetting> settings)
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

    public static void ProcessNs(this IEnumerable<LanguageSetting> allSettings,  NsTreeItem node, string ns, int depth = 1, int customDepth = 0)
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

        var namespaces = allSettings.Where(o => o.Namespace.StartsWith(ns + ".")).Select(o => o.Namespace.Substring(ns.Length + 1).Split('.')[0]).Distinct().OrderBy(o => o).ToList();

        if (!namespaces.Any())
        {
            thisNode.ImagePath = "Assets/Images/translation.png";
            thisNode.Settings = allSettings.Where(o => o.Namespace == thisNode.Namespace);
            return;
        }

        var applicableSettings = allSettings.Where(o => o.Namespace.StartsWith(ns + ".")).ToList();
        
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
