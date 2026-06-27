using System;
using System.Collections;
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
        // Handle CollectionView and other non-generic enumerables by iterating non-generic IEnumerable
        if (value is IEnumerable enumerable)
        {
            List<NsFlatItem> result = new();

            foreach (var v in enumerable)
            {
                if (v is NsTreeItem item)
                {
                    Flatten(item, "", 0, result);
                }
            }

            return result;
        }

        return Enumerable.Empty<NsFlatItem>();
    }

    private void Flatten(NsTreeItem item, string parentNs, int depth, List<NsFlatItem> result)
    {
        string fullKey = string.IsNullOrEmpty(parentNs) ? item.Name : $"{parentNs}.{item.Name}";
        bool isLeaf = !item.Items.Any();

        // ponytail: only show leaf keys in list view — parent namespace nodes are noise
        if (isLeaf)
        {
            result.Add(new NsFlatItem
            {
                Depth = depth,
                FullKey = fullKey,
                IsLeaf = true,
                Source = item,
                DisplayKey = item.Namespace
            });
        }

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
