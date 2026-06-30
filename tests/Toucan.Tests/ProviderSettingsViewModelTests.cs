using System.Collections.Generic;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.ViewModels;
using Toucan.Services;
using Xunit;

namespace Toucan.Tests
{
    public class FakeDialogService : IDialogService
    {
        public string? LastSelected { get; set; }
        public string? SelectFolder(string? initialPath)
        {
            return LastSelected;
        }

        public string? SelectFile(string? initialPath, string filter = "All Files (*.*)|*.*")
        {
            return null;
        }

        public string? ShowPrompt(string title, string message, string defaultValue = "") => null;

        public bool ShowAbout() => true;

        public bool ShowNewProject(IProjectService projectService, out NewProjectViewModel? resultVm)
        {
            resultVm = null;
            return false;
        }

        public bool ShowOptions(AppOptions options, string currentPath, out AppOptions? updatedOptions)
        {
            updatedOptions = null;
            return false;
        }

        public bool ShowPreTranslate(PreTranslateViewModel vm) => false;

        public bool ShowProviderSettings() => false;

        public bool ShowProjectProperties(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null) => false;

        public bool ShowImportProject(out ImportProjectViewModel? resultVm)
        {
            resultVm = null;
            return false;
        }

        public string? ShowLanguagePrompt(string title, string message, IEnumerable<TranslationItem>? existingTranslations) => null;

        public LanguageManagerViewModel? ShowManageLanguages(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null) => null;

        public void Shutdown() { }
    }

    public class FakeProviderSettingsService : IProviderSettingsService
    {
        public List<ProviderSettings> App = new();
        public Dictionary<string, List<ProviderSettings>> Projects = new();

        public IEnumerable<ProviderSettings> LoadAppProviderSettings() => App;
        public void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings)
        {
            App = settings.ToList();
        }

        public IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath)
        {
            return Projects.ContainsKey(projectPath) ? Projects[projectPath] : Enumerable.Empty<ProviderSettings>();
        }

        public void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings)
        {
            Projects[projectPath] = settings.ToList();
        }
    }

    public class ProviderSettingsViewModelTests
    {
        private static readonly ITranslationProviderRegistry _registry = new TranslationProviderRegistry();

        private static ProviderSettingsViewModel CreateVm(FakeProviderSettingsService? svc = null)
        {
            svc ??= new FakeProviderSettingsService();
            return new ProviderSettingsViewModel(svc, new SecureStorageService(), new FakeDialogService(), _registry);
        }

        [Fact]
        public void LoadAppSettings_PopulatesProviders()
        {
            var svc = new FakeProviderSettingsService();
            svc.App.Add(new ProviderSettings { Provider = "Google" });

            var vm = new ProviderSettingsViewModel(svc, new SecureStorageService(), new FakeDialogService(), _registry);

            // Built-in providers are merged in; Google should be present
            Assert.Contains(vm.Providers, p => p.Provider == "Google");
            Assert.Equal("Google", vm.Selected?.Provider);
        }

        [Fact]
        public void Constructor_PopulatesAllBuiltInProviders()
        {
            var vm = CreateVm();

            // All 5 built-in providers should appear
            Assert.Contains(vm.Providers, p => p.Provider == "Google");
            Assert.Contains(vm.Providers, p => p.Provider == "DeepL");
            Assert.Contains(vm.Providers, p => p.Provider == "Microsoft");
            Assert.Contains(vm.Providers, p => p.Provider == "OpenAI");
            Assert.Contains(vm.Providers, p => p.Provider == "Custom");
            Assert.True(vm.Providers.Count >= 5);
        }

        [Fact]
        public void DefaultValues_PrePopulated_ForDeepL()
        {
            var vm = CreateVm();

            var deepl = vm.Providers.First(p => p.Provider == "DeepL");
            Assert.Equal("https://api.deepl.com/v2/translate", deepl.Options["endpoint"]);
        }

        [Fact]
        public void DefaultValues_PrePopulated_ForOpenAI()
        {
            var vm = CreateVm();

            var openai = vm.Providers.First(p => p.Provider == "OpenAI");
            Assert.Equal("https://api.openai.com/v1", openai.Options["endpoint"]);
            Assert.Equal("gpt-4o-mini", openai.Options["model"]);
        }

        [Fact]
        public void AddProvider_AddsNewEntry()
        {
            var vm = CreateVm();
            int before = vm.Providers.Count;

            var customDef = _registry.GetByName("Custom");
            vm.AddProviderCommand.Execute(customDef);

            Assert.Equal(before + 1, vm.Providers.Count);
            Assert.Equal("Custom", vm.Selected?.Provider);
        }

        [Fact]
        public void RemoveSelected_RemovesCurrentProvider()
        {
            var vm = CreateVm();
            vm.Selected = vm.Providers.First(p => p.Provider == "DeepL");

            vm.RemoveSelectedCommand.Execute(null);

            Assert.DoesNotContain(vm.Providers, p => p.Provider == "DeepL");
        }

        [Fact]
        public void Save_FlushesFieldEditsToProvider()
        {
            var svc = new FakeProviderSettingsService();
            var vm = new ProviderSettingsViewModel(svc, new SecureStorageService(), new FakeDialogService(), _registry);

            // Select OpenAI and modify an option via the OptionItems
            vm.Selected = vm.Providers.First(p => p.Provider == "OpenAI");

            // The OptionItems should be populated (endpoint, model, prompt)
            var endpointItem = vm.OptionItems.FirstOrDefault(i => i.Key == "endpoint");
            Assert.NotNull(endpointItem);
            endpointItem!.Value = "http://localhost:11434/v1";

            // Save should flush the edit back
            vm.SaveCommand.Execute(null);

            var saved = svc.App.First(p => p.Provider == "OpenAI");
            Assert.Equal("http://localhost:11434/v1", saved.Options["endpoint"]);
        }

        [Fact]
        public void Save_CallsSaveApp_WhenNotProjectScope()
        {
            var svc = new FakeProviderSettingsService();
            var vm = new ProviderSettingsViewModel(svc, new SecureStorageService(), new FakeDialogService(), _registry);

            vm.SaveCommand.Execute(null);

            // All built-in providers are saved
            Assert.True(svc.App.Count > 0);
        }

        [Fact]
        public void LoadProjectSettings_UsesProjectPath()
        {
            var svc = new FakeProviderSettingsService();
            svc.Projects["/tmp/projectA"] = new List<ProviderSettings> { new ProviderSettings { Provider = "Google", Options = { ["custom_opt"] = "val" } } };

            var dialogs = new FakeDialogService { LastSelected = "/tmp/projectA" };
            var vm = new ProviderSettingsViewModel(svc, new SecureStorageService(), dialogs, _registry);

            vm.ProjectPath = "/tmp/projectA";
            vm.LoadProjectSettingsCommand.Execute(null);

            // Built-in providers are merged with project settings
            Assert.Contains(vm.Providers, p => p.Provider == "Google");
            Assert.True(vm.ProjectScope);
        }

        [Fact]
        public void SelectedDefinition_MatchesSelectedProvider()
        {
            var vm = CreateVm();
            vm.Selected = vm.Providers.First(p => p.Provider == "DeepL");

            Assert.NotNull(vm.SelectedDefinition);
            Assert.Equal("DeepL", vm.SelectedDefinition!.Name);
            Assert.Equal("DeepL", vm.SelectedDefinition.DisplayName);
        }

        [Fact]
        public void AddOption_AddsEditableEntry()
        {
            var vm = CreateVm();
            vm.Selected = vm.Providers.First(p => p.Provider == "Google");

            int before = vm.OptionItems.Count;
            vm.AddOptionCommand.Execute(null);

            Assert.Equal(before + 1, vm.OptionItems.Count);
            Assert.False(vm.OptionItems.Last().IsSchemaField);
        }

        [Fact]
        public void RemoveOption_RemovesNonSchemaItem()
        {
            var vm = CreateVm();
            vm.Selected = vm.Providers.First(p => p.Provider == "Google");
            vm.AddOptionCommand.Execute(null);

            var added = vm.OptionItems.Last();
            vm.RemoveOptionCommand.Execute(added);

            Assert.DoesNotContain(vm.OptionItems, i => ReferenceEquals(i, added));
        }

        [Fact]
        public void SecretItems_PopulatedFromSchema()
        {
            var vm = CreateVm();
            vm.Selected = vm.Providers.First(p => p.Provider == "Google");

            // Google has one secret: api_key
            Assert.Single(vm.SecretItems);
            Assert.Equal("api_key", vm.SecretItems[0].Key);
            Assert.True(vm.SecretItems[0].IsSchemaField);
        }
    }
}
