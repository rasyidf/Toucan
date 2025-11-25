using System.Collections.Generic;

namespace Toucan.Core.Models;

public class ProviderSettings
{
    // Provider id/name (e.g. "DeepL", "Google")
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Non-secret provider options stored in clear text (endpoint override, region, etc.).
    /// </summary>
    public IDictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Secret fields for this provider. Secrets are stored encrypted on disk by the service implementation.
    /// Keys are logical names such as "api_key", "auth_key".
    /// </summary>
    public IDictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
}
