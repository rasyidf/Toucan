using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Toucan.Core.Models;

namespace Toucan.Converters;

public class TreeItemtoListItemConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<NsTreeItem> roots)
        {
            var result = new List<NsFlatItem>();
            foreach (var item in roots)
            {
                Flatten(item, "", 0, result);
            }

            return result;
        }

        return Enumerable.Empty<NsFlatItem>();
    }

    private void Flatten(NsTreeItem item, string parentNs, int depth, List<NsFlatItem> result)
    {
        string fullKey = string.IsNullOrEmpty(parentNs) ? item.Name : $"{parentNs}.{item.Name}";
        bool isLeaf = !item.Items.Any();

        result.Add(new NsFlatItem
        {
            Depth = depth,
            FullKey = fullKey,
            IsLeaf = isLeaf,
            Source = item,
            DisplayKey = $"{new string(' ', depth * 2)}{(depth > 0 ? "└ " : "")}{item.Name}"
        });

        foreach (var child in item.Items)
        {
            Flatten(child, fullKey, depth + 1, result);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
