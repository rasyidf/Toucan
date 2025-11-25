using System.Collections.Generic;
using System.Linq;
using Toucan.Core.Models;
using Toucan.ViewModels;
using Toucan.Services;
using Xunit;

namespace Toucan.Tests
{
    public class FakeDialogService : IDialogService
    {
        public string? LastSelected { get; set; }
        public string? SelectFolder(string initialPath)
        {
            return LastSelected;
        }

        public bool? ShowDialog(System.Windows.Window dialog)
        {
            return true;
        }

        public string? SelectFile(string initialPath, string filter = "All Files (*.*)|*.*")
        {
            return null;
        }
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
        [Fact]
        public void LoadAppSettings_PopulatesProviders()
        {
            var svc = new FakeProviderSettingsService();
            svc.App.Add(new ProviderSettings { Provider = "Google" });

            var secure = new SecureStorageService();
            var dialogs = new FakeDialogService();

            var vm = new ProviderSettingsViewModel(svc, secure, dialogs);

            Assert.Single(vm.Providers);
            Assert.Equal("Google", vm.Selected?.Provider);
        }

        [Fact]
        public void Save_CallsSaveApp_WhenNotProjectScope()
        {
            var svc = new FakeProviderSettingsService();
            var secure = new SecureStorageService();
            var dialogs = new FakeDialogService();

            var vm = new ProviderSettingsViewModel(svc, secure, dialogs);
            vm.Providers.Add(new ProviderSettings { Provider = "DeepL" });

            vm.SaveCommand.Execute(null);

            Assert.Single(svc.App);
            Assert.Equal("DeepL", svc.App[0].Provider);
        }

        [Fact]
        public void LoadProjectSettings_UsesProjectPath()
        {
            var svc = new FakeProviderSettingsService();
            svc.Projects["/tmp/projectA"] = new List<ProviderSettings> { new ProviderSettings { Provider = "ProjectGoogle" } };

            var secure = new SecureStorageService();
            var dialogs = new FakeDialogService { LastSelected = "/tmp/projectA" };

            var vm = new ProviderSettingsViewModel(svc, secure, dialogs);

            vm.ProjectPath = "/tmp/projectA";
            vm.LoadProjectSettingsCommand.Execute(null);

            Assert.Single(vm.Providers);
            Assert.Equal("ProjectGoogle", vm.Selected?.Provider);
            Assert.True(vm.ProjectScope);
        }
    }
}
