namespace Toucan.Core.Models;

public class NsFlatItem
{
    public required string DisplayKey { get; set; }
    public required string FullKey { get; set; }
    public int Depth { get; set; }
    public bool IsLeaf { get; set; }
    public required NsTreeItem Source { get; set; }
}
