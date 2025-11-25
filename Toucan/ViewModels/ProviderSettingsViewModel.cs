using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Models;
using Toucan.Services;

namespace Toucan.ViewModels
{
    public partial class ProviderSettingsViewModel : ObservableObject
    {
        private readonly IProviderSettingsService _service;
        private readonly ISecureStorageService _secure;
        private readonly IDialogService _dialogs;

        public ObservableCollection<ProviderSettings> Providers { get; } = new();

        [ObservableProperty]
        private ProviderSettings? selected;

        [ObservableProperty]
        private bool projectScope;

        [ObservableProperty]
        private string projectPath = string.Empty;

        public ProviderSettingsViewModel(IProviderSettingsService service, ISecureStorageService secure, IDialogService dialogs)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _secure = secure ?? throw new ArgumentNullException(nameof(secure));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            LoadAppSettings();
        }

        [RelayCommand]
        private void LoadAppSettings()
        {
            Providers.Clear();
            foreach (var p in _service.LoadAppProviderSettings())
                Providers.Add(p);

            Selected = Providers.FirstOrDefault();
            ProjectScope = false;
        }

        [RelayCommand]
        private void LoadProjectSettings()
        {
            var path = ProjectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folder = _dialogs.SelectFolder(Environment.CurrentDirectory);
                if (!string.IsNullOrEmpty(folder)) path = folder;
            }

            if (string.IsNullOrWhiteSpace(path)) return;

            Providers.Clear();
            foreach (var p in _service.LoadProjectProviderSettings(path))
                Providers.Add(p);

            Selected = Providers.FirstOrDefault();
            ProjectScope = true;
            ProjectPath = path;
        }

        [RelayCommand]
        private void Save()
        {
            if (ProjectScope)
            {
                if (string.IsNullOrWhiteSpace(ProjectPath)) return;
                _service.SaveProjectProviderSettings(ProjectPath, Providers);
            }
            else
            {
                _service.SaveAppProviderSettings(Providers);
            }
        }

        [RelayCommand]
        private void AddProvider()
        {
            var newP = new ProviderSettings
            {
                Provider = "Custom",
                Options = new Dictionary<string, string>(),
                Secrets = new Dictionary<string, string>()
            };

            Providers.Add(newP);
            Selected = newP;
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (Selected == null) return;
            Providers.Remove(Selected);
            Selected = Providers.FirstOrDefault();
        }

        [RelayCommand]
        private void SelectProjectFolder()
        {
            var folder = _dialogs.SelectFolder(ProjectPath ?? Environment.CurrentDirectory);
            if (!string.IsNullOrEmpty(folder)) ProjectPath = folder;
        }
    }
}
