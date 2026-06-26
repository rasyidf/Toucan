using Toucan.Extensions;

namespace Toucan.Core.Models;

public class NsTreeItem
{
    public NsTreeItem? Parent { get; set; }
    public bool IsLoaded { get; set; }
    public bool IsExpanded { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? ImagePath { get; set; }

    private List<NsTreeItem> _storage = [];

    public IEnumerable<NsTreeItem> Items
    {
        get
        {
            if (!IsLoaded && HeldSetttings != null)
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
        set => _storage = value.ToList();
    }

    public IEnumerable<TranslationItem>? Settings { get; set; }
    public List<TranslationItem>? HeldSetttings;

    public bool HasItems => _storage.Count > 0;
    public bool HasParent => Parent != null;

    public void ToJson(Dictionary<string, object> parent, string language)
    {
        if (HasItems || Items.Any())
        {
            var node = new Dictionary<string, object>();
            parent[Name] = node;
            foreach (var item in Items.ToList())
                item.ToJson(node, language);
            if (node.Count == 0)
                parent.Remove(Name);
        }
        else if (Settings != null)
        {
            var setting = Settings.FirstOrDefault(o => o.Language == language);
            if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                parent[Name] = setting.Value;
        }
    }

    public override string ToString() => $"{Name} | {Namespace} | Items: {_storage.Count}";

    public void AddChild(NsTreeItem child) => _storage.Add(child);
    public void Clear() => _storage.Clear();
}
