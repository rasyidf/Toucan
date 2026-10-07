using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class RecentProjectServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-recent-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private RecentProjectService Create() => new(Path.Combine(_root, "recent.json"));

    [Fact]
    public void Pinned_ProjectsComeFirst_EvenWhenOlder()
    {
        var service = Create();
        string a = Folder("a"), b = Folder("b"), c = Folder("c");
        service.Add(a);
        service.Add(b);
        service.Add(c);

        service.SetPinned(a, true);

        Assert.Equal([a, c, b], service.LoadRecent().Select(p => p.Path));
    }

    [Fact]
    public void Pinned_ProjectsDoNotCountAgainstTheLimit()
    {
        var service = Create();
        service.Limit = 2;
        var pinned = Folder("pinned");
        service.Add(pinned);
        service.SetPinned(pinned, true);

        foreach (var n in new[] { "a", "b", "c", "d" }) service.Add(Folder(n));

        var paths = service.LoadRecent().Select(p => Path.GetFileName(p.Path)).ToList();
        Assert.Equal(["pinned", "d", "c"], paths);
    }

    [Fact]
    public void Pin_SurvivesReopeningTheProject_AndARestart()
    {
        var service = Create();
        var a = Folder("a");
        service.Add(a);
        service.SetPinned(a, true);

        service.Add(a);

        Assert.True(Create().LoadRecent().Single().IsPinned);
    }

    [Fact]
    public void Unpin_ReturnsTheProjectToItsRecencyPosition()
    {
        var service = Create();
        string a = Folder("a"), b = Folder("b");
        service.Add(a);
        service.Add(b);
        service.SetPinned(a, true);

        service.SetPinned(a, false);

        Assert.Equal([b, a], service.LoadRecent().Select(p => p.Path));
    }

    [Fact]
    public void Clear_KeepsPinned_OnlyWhenAsked()
    {
        var service = Create();
        string a = Folder("a"), b = Folder("b");
        service.Add(a);
        service.Add(b);
        service.SetPinned(a, true);

        service.Clear(keepPinned: true);
        Assert.Equal([a], service.LoadRecent().Select(p => p.Path));

        service.Clear(keepPinned: false);
        Assert.Empty(service.LoadRecent());
    }

    [Fact]
    public void LoadRecent_DropsMissingFolders_ExceptPinnedOnes()
    {
        var service = Create();
        string gone = Folder("gone"), pinnedGone = Folder("pinned-gone");
        service.Add(gone);
        service.Add(pinnedGone);
        service.SetPinned(pinnedGone, true);
        Directory.Delete(gone);
        Directory.Delete(pinnedGone);

        Assert.Equal([pinnedGone], service.LoadRecent().Select(p => p.Path));
    }

    [Fact]
    public void PrimaryLanguage_IsStoredPerProject()
    {
        var service = Create();
        var a = Folder("a");
        service.Add(a);

        service.SetPrimaryLanguage(a, "id-ID");

        Assert.Equal("id-ID", Create().LoadRecent().Single().PrimaryLanguage);
    }

    [Fact]
    public void Limit_IsAtLeastOne()
    {
        var service = Create();
        service.Limit = 0;
        Assert.Equal(1, service.Limit);
    }
}

public class RecentProjectsViewModelTests
{
    [AvaloniaFact]
    public async Task TogglePin_PinsThenUnpins_AndKeepsPinnedFirst()
    {
        using var host = new TestHost();
        var older = host.CreateJsonProject("older", ("en", """{"a": "A"}"""));
        var newer = host.CreateJsonProject("newer", ("en", """{"a": "A"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(older);
        await vm.OpenProjectAsync(newer);
        Assert.Equal(newer, vm.RecentProjects[0].Path);

        vm.TogglePinRecentProjectCommand.Execute(older);
        Assert.Equal(older, vm.RecentProjects[0].Path);
        Assert.True(vm.RecentProjects[0].IsPinned);

        vm.TogglePinRecentProjectCommand.Execute(older);
        Assert.Equal(newer, vm.RecentProjects[0].Path);
    }

    [AvaloniaFact]
    public async Task Clear_FollowsTheKeepPinnedSetting()
    {
        using var host = new TestHost();
        var a = host.CreateJsonProject("a", ("en", """{"a": "A"}"""));
        var b = host.CreateJsonProject("b", ("en", """{"a": "A"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(a);
        await vm.OpenProjectAsync(b);
        vm.TogglePinRecentProjectCommand.Execute(a);

        vm.ClearRecentProjectsCommand.Execute(null);
        Assert.Equal([a], vm.RecentProjects.Select(p => p.Path));

        vm.AppOptions.ClearRecentKeepsPinned = false;
        vm.ClearRecentProjectsCommand.Execute(null);
        Assert.Empty(vm.RecentProjects);
    }

    [AvaloniaFact]
    public async Task OpeningAProject_RecordsItsLanguage_ForDetection()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("lang", ("id", """{"a": "A"}"""), ("en", """{"a": "A"}"""));
        var vm = host.CreateViewModel();
        vm.AppOptions.DefaultLanguage = "id";

        await vm.OpenProjectAsync(folder);

        var recorded = host.Services.GetRequiredService<IRecentProjectService>().LoadRecent().Single().PrimaryLanguage;
        Assert.Equal(vm.PrimaryLanguage, recorded);
    }

    [AvaloniaFact]
    public async Task PreferredDefaultLanguage_FollowsTheMostRecentProject_OnlyWhenToggledOn()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("detect", ("fr", """{"a": "A"}"""));
        var vm = host.CreateViewModel();
        vm.AppOptions.DefaultLanguage = "en-US";
        await vm.OpenProjectAsync(folder);

        vm.AppOptions.DetectLanguageFromRecent = false;
        Assert.Equal("en-US", vm.PreferredDefaultLanguage());

        vm.AppOptions.DetectLanguageFromRecent = true;
        Assert.Equal("fr", vm.PreferredDefaultLanguage());
    }
}
