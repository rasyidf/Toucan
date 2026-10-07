using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class SecureStorageServiceTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        var dir = Directory.CreateTempSubdirectory("toucan-key-").FullName;
        try
        {
            var svc = new SecureStorageService(Path.Combine(dir, "secret.key"));
            var cipher = svc.Protect("sk-test-123");

            Assert.NotEqual("sk-test-123", cipher);
            Assert.Equal("sk-test-123", svc.Unprotect(cipher));
            // A new instance reads the same key file.
            Assert.Equal("sk-test-123", new SecureStorageService(Path.Combine(dir, "secret.key")).Unprotect(cipher));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void KeyFile_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("toucan-key-").FullName;
        try
        {
            var keyFile = Path.Combine(dir, "secret.key");
            new SecureStorageService(keyFile).Protect("x");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyFile));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Unprotect_ReadsLegacyBase64()
    {
        var svc = new SecureStorageService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "k"));
        Assert.Equal("abc", svc.Unprotect(Convert.ToBase64String("abc"u8.ToArray())));
        Assert.Equal(string.Empty, svc.Unprotect(string.Empty));
    }
}

public class PaginationViewModelTests
{
    [Fact]
    public void Paging_WalksThroughPages()
    {
        var paging = new PaginationViewModel<int>(10, Enumerable.Range(1, 25));

        Assert.Equal(3, paging.Pages);
        Assert.Equal(Enumerable.Range(1, 10), paging.PageData);
        paging.NextPage();
        paging.NextPage();
        Assert.Equal([21, 22, 23, 24, 25], paging.PageData);
        Assert.False(paging.HasNextPage);
        paging.GoTo(99);
        Assert.Equal(3, paging.Page);
    }

    [Fact]
    public void SwapData_ClampsCurrentPage()
    {
        var paging = new PaginationViewModel<int>(10, Enumerable.Range(1, 50));
        paging.LastPage();
        paging.SwapData(Enumerable.Range(1, 5));
        Assert.Equal(1, paging.Page);
        Assert.Equal("Showing all · 5 items", paging.PageMessage);
    }
}

public class LanguageManagerViewModelTests
{
    private static List<TranslationItem> Items() =>
    [
        new() { Namespace = "a", Language = "en", Value = "A" },
        new() { Namespace = "a", Language = "fr", Value = "" },
    ];

    [Fact]
    public void AddThenRemove_SameLanguage_IsANoOp()
    {
        var vm = new LanguageManagerViewModel(Items(), "en") { FilterText = "de-DE" };
        vm.AddLanguageCommand.Execute(vm.FilteredCultures.First(c => c.Culture?.Name == "de-DE"));
        vm.RemoveLanguageCommand.Execute(vm.Languages.Single(l => l.Code == "de-DE"));

        Assert.Empty(vm.AddedLanguages);
        Assert.Empty(vm.RemovedLanguages);
    }

    [Fact]
    public void PrimaryLanguage_CannotBeRemoved()
    {
        var vm = new LanguageManagerViewModel(Items(), "en");
        vm.RemoveLanguageCommand.Execute(vm.Languages.Single(l => l.Code == "en"));
        Assert.Contains(vm.Languages, l => l.Code == "en");

        vm.SetPrimaryCommand.Execute(vm.Languages.Single(l => l.Code == "fr"));
        Assert.Equal("fr", vm.PrimaryLanguage);
    }
}

public class StatisticsViewModelTests
{
    [Fact]
    public void ComputesPerLanguageCompletion()
    {
        var stats = new StatisticsViewModel(
        [
            new TranslationItem { Namespace = "a", Language = "en", Value = "A", IsApproved = true },
            new TranslationItem { Namespace = "b", Language = "en", Value = "B" },
            new TranslationItem { Namespace = "a", Language = "fr", Value = "" },
            new TranslationItem { Namespace = "b", Language = "fr", Value = "Bé" },
        ]);

        Assert.Equal("2", stats.TotalKeys);
        Assert.Equal(100, stats.Languages.Single(l => l.Language == "en").Percent);
        Assert.Equal(50, stats.Languages.Single(l => l.Language == "fr").Percent);
        Assert.Equal("bad", stats.Languages.Single(l => l.Language == "fr").Health);
    }
}

public class KeybindingServiceTests
{
    [Fact]
    public void EveryShortcutIsUnique()
    {
        var shortcuts = KeybindingService.GetDefinitions().Select(d => d.Shortcut).ToList();
        Assert.Equal(shortcuts.Count, shortcuts.Distinct().Count());
    }
}
