using System;
using System.Collections.Generic;
using System.Text;
using Toucan.Core.Options;

namespace Toucan.Services;

internal interface IPreferenceService
{
    AppOptions Load();
    void Save(AppOptions options);
}

internal class PreferenceService : IPreferenceService
{
    public AppOptions Load() => AppOptions.LoadFromDisk();
    public void Save(AppOptions options) => options.ToDisk();
}
