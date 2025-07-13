
namespace Toucan.Core.Models;

public class NsFlatItem
{
    public string DisplayKey { get; set; }      // "   └ save"
    public string FullKey { get; set; }         // "app.main_dialog.save"
    public int Depth { get; set; }
    public bool IsLeaf { get; set; }

    public NsTreeItem Source { get; set; }
}
