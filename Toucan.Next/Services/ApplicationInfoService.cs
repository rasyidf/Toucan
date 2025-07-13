using System.Diagnostics;
using System.Reflection;

using Toucan.Contracts.Services;

using OSVersionHelper;

using Windows.ApplicationModel;

namespace Toucan.Services;

public class ApplicationInfoService : IApplicationInfoService
{
    public ApplicationInfoService()
    {
    }

    public Version GetVersion()
    {
        if (WindowsVersionHelper.HasPackageIdentity)
        {
            // Packaged application
            // Set the app version in Toucan.Packaging > Package.appxmanifest > Packaging > PackageVersion
            var packageVersion = Package.Current.Id.Version;
            return new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }

        // Set the app version in OPEdit > Properties > Package > PackageVersion
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var version = FileVersionInfo.GetVersionInfo(assemblyLocation).FileVersion;
        return new Version(version);
    }
}
