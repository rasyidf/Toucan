using System.Collections.Generic;
using System.Linq;
using Toucan.ViewModels;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Tests;

internal class FakeProjectService : IProjectService
{
    public string? LastFolder { get; private set; }
    public IEnumerable<string>? LastLanguages { get; private set; }
    public SaveStyles? LastStyle { get; private set; }
    public bool LastCreateManifest { get; private set; }

    public List<TranslationItem> Load(string folder) => new List<TranslationItem>();

    public void CreateLanguage(string folder, string language, SaveStyles style = SaveStyles.Json) { }

    public void CreateProject(string folder, IEnumerable<string> languages, SaveStyles style = SaveStyles.Json, bool createManifest = false)
    {
        LastFolder = folder;
        LastLanguages = languages?.ToList();
        LastStyle = style;
        LastCreateManifest = createManifest;
    }

    public void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations) { }
}

public class NewProjectViewModelTests
{
    [Fact]
    public void Constructor_Adds_DefaultLanguages()
    {
        var vm = new NewProjectViewModel();

        // Ensure it always contains at least en-US
        Assert.Contains("en-US", vm.Languages);
        // Also suggestions should include other helpful languages
        Assert.Contains("id-ID", vm.Languages);
        Assert.True(vm.Languages.Count >= 1);
    }

    [Fact]
    public void IsValid_Updates_When_Name_And_Folder_Are_Set()
    {
        var vm = new NewProjectViewModel();

        // initially invalid
        Assert.False(vm.IsValid);

        vm.ProjectName = "MyProject";
        vm.ProjectFolder = "C:\\temp\\proj";

        Assert.True(vm.IsValid);
    }

    [Fact]
    public void CreateProject_CallsProjectService_WithManifestFlag()
    {
        var fake = new FakeProjectService();
        var vm = new NewProjectViewModel(fake);

        vm.ProjectName = "MyProject";
        vm.ProjectFolder = "C:\\temp\\proj";
        vm.Languages.Clear();
        vm.Languages.Add("en-US");
        vm.SelectedFramework = "JSON";
        vm.CreateManifest = true;

        vm.CreateProject();

        Assert.Equal("C:\\temp\\proj", fake.LastFolder);
        Assert.NotNull(fake.LastLanguages);
        Assert.Contains("en-US", fake.LastLanguages);
        Assert.True(fake.LastCreateManifest);
    }
}
