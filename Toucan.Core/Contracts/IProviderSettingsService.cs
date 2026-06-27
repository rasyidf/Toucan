using System.Collections.Generic;
using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface IProviderSettingsService
{
    IEnumerable<ProviderSettings> LoadAppProviderSettings();
    void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings);

    IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath);
    void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings);
}
