using OPEditor.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPEdit.Core.Models;

public class NsTreeItem
{
    public NsTreeItem Parent { get; set; }
    public bool IsLoaded { get; set; }

    public bool IsExpanded { get; set; }
    public string Name { get; set; }

    public string Namespace { get; set; }
    public string ImagePath { get; set; }
    private List<NsTreeItem> _storage { get; set; } = new List<NsTreeItem>();

    public IEnumerable<NsTreeItem> Items
    {
        get
        {
            if (!IsLoaded)
                if (HeldSetttings != null)
                {
                    HeldSetttings.ProcessNs(this, Namespace);

                    var copy = _storage.ToList();
                    _storage.Clear();
                    foreach (var child in copy)
                    {
                        child.Parent = this;
                        _storage.AddRange(child.Items);
                    }

                    HeldSetttings = null;
                }

            return _storage;
        }
        set { _storage = value.ToList(); }
    }

    public IEnumerable<LanguageSetting> Settings { get; set; }

    public bool HasItems => _storage == null ? false : _storage.Count > 0;
    public bool HasParent => Parent != null;

    public NsTreeItem() { }


    public void ToJson(Dictionary<string, dynamic> parent, string language)
    {

        if (HasItems || Items.Any())
        {
            var node = new Dictionary<string, dynamic>();
            parent.Add(Name, node);
            foreach (var item in Items.ToList())
            {
                item.ToJson(node, language);
            }
            if (!node.Any())
                parent.Remove(Name);
        }
        else
        {
            if (Settings != null && Settings.Any())
            {
                var setting = Settings.FirstOrDefault(o => o.Language == language);
                if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                    parent.Add(Name, setting.Value);
            }
        }
    }


    public override string ToString()
    {
        return $"{Name} | {Namespace} | Items: {(_storage == null ? 0 : _storage.Count)}";
    }

    public List<LanguageSetting> HeldSetttings;

    public void AddChild(NsTreeItem child)
    {
        _storage.Add(child);
    }
    public void Clear()
    {
        _storage.Clear();
    }

}
