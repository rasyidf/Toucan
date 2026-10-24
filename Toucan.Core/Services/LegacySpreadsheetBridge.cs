using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Old static Excel API metadata only. Modern exchange reads and writes neutral tables.</summary>
internal static class LegacySpreadsheetBridge
{
    public static TranslationItem Normalize(TranslationItem item)
    {
        if (item.Identity != null || item.FormatData?.GetValueOrDefault("babel.package") is not { } package) return item;
        var copy = ResourceEntryOperations.Copy(item);
        copy.Identity = new ResourceEntryIdentity { Package = package,
            Path = [ResourcePathSegment.Named(item.FormatData.GetValueOrDefault("babel.key") ?? item.Namespace)] };
        return copy;
    }
    public static List<TranslationItem> Items(SpreadsheetTable table)
    {
        var multiple = table.Units.Select(u => u.Package).Distinct().Count() > 1;
        return table.Units.Select(u => new TranslationItem
        {
            Namespace = (multiple ? u.Package + "." : "") + u.Key, Language = u.Language,
            Value = u.Value ?? "", Comment = u.Comment ?? "", IsApproved = u.Approved ?? false,
            FormatData = new() { ["babel.package"] = u.Package, ["babel.key"] = u.Key,
                ["exchange.comment"] = (u.Comment != null).ToString(), ["exchange.approved"] = (u.Approved != null).ToString() }
        }).ToList();
    }
}
