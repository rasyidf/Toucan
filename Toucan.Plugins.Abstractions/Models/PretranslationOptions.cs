namespace Toucan.Core.Models;

public class PretranslationOptions
{
    public bool Overwrite { get; set; }
    public bool PreviewOnly { get; set; }
    public IDictionary<string, string>? ProviderOptions { get; set; }
}
