using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>MIG-02: a project from an earlier release opens in the editor without unsaved changes unless opening it really left something to save.</summary>
public sealed class ReleaseUpgradeUiTests
{
    private static string Fixture(string release, string project)
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Toucan.slnx"))) root = Path.GetDirectoryName(root);
        return Path.Combine(root!, "tests", "fixtures", "releases", release, "projects", project);
    }

    private static string CopyInto(TestHost host, string release, string project)
    {
        var source = Fixture(release, project);
        var target = Directory.CreateDirectory(Path.Combine(host.Root, $"{release}-{project}")).FullName;
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return target;
    }

    [AvaloniaFact]
    public async Task A023ProjectWithSidecarCommentsOpensWithNothingToSave()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(CopyInto(host, "0.23.0", "json"));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.AllTranslation, t => t.Comment == "Checked by Lea");
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task A023ProjectWithMergedCopiesOpensReadyToSave()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var folder = CopyInto(host, "0.23.0", "yaml");
        await vm.OpenProjectAsync(folder);
        Dispatcher.UIThread.RunJobs();

        // The open warning says the next save sets the copies aside, so that save must be possible.
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.False(File.Exists(Path.Combine(folder, "en.yaml")));
    }
}
