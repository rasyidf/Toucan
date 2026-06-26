namespace Toucan.Core.Models;

public class ProviderSettings
{
    public string Provider { get; set; } = string.Empty;
    public Dictionary<string, string> Options { get; set; } = [];
    public Dictionary<string, string> Secrets { get; set; } = [];
}
