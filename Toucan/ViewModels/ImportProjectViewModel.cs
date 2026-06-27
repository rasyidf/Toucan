using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class ImportProjectViewModel : ObservableObject
{
    private readonly IEnumerable<IFrameworkProfile> _profiles;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string folder = string.Empty;

    [ObservableProperty]
    private IFrameworkProfile? detectedProfile;

    [ObservableProperty]
    private IFrameworkProfile? selectedProfile;

    [ObservableProperty]
    private bool isDetecting;

    public ObservableCollection<IFrameworkProfile> AvailableProfiles { get; } = new();
    public ObservableCollection<DiscoveredFile> DiscoveredFiles { get; } = new();
    public ObservableCollection<string> DetectedLanguages { get; } = new();

    public bool IsValid => !string.IsNullOrWhiteSpace(Folder) && SelectedProfile != null && DiscoveredFiles.Count > 0;

    public ImportProjectViewModel(IEnumerable<IFrameworkProfile> profiles, IDialogService dialogService)
    {
        _profiles = profiles;
        _dialogService = dialogService;
        foreach (var p in profiles)
            AvailableProfiles.Add(p);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var selected = _dialogService.SelectFolder(Folder);
        if (!string.IsNullOrEmpty(selected))
        {
            Folder = selected;
            RunDetection();
        }
    }

    partial void OnFolderChanged(string value)
    {
        if (Directory.Exists(value))
            RunDetection();
    }

    partial void OnSelectedProfileChanged(IFrameworkProfile? value)
    {
        if (value != null && Directory.Exists(Folder))
            RefreshDiscoveredFiles(value);
        OnPropertyChanged(nameof(IsValid));
    }

    private void RunDetection()
    {
        IsDetecting = true;
        DiscoveredFiles.Clear();
        DetectedLanguages.Clear();

        // Score all profiles against the folder
        var scored = _profiles
            .Select(p => (Profile: p, Score: p.DetectionScore(Folder)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        DetectedProfile = scored.FirstOrDefault().Profile;
        SelectedProfile = DetectedProfile;

        if (SelectedProfile != null)
            RefreshDiscoveredFiles(SelectedProfile);

        IsDetecting = false;
        OnPropertyChanged(nameof(IsValid));
    }

    private void RefreshDiscoveredFiles(IFrameworkProfile profile)
    {
        DiscoveredFiles.Clear();
        DetectedLanguages.Clear();

        foreach (var file in profile.DiscoverFiles(Folder))
        {
            DiscoveredFiles.Add(file);
            if (!DetectedLanguages.Contains(file.Language))
                DetectedLanguages.Add(file.Language);
        }

        OnPropertyChanged(nameof(IsValid));
    }

    /// <summary>Returns the import result: folder path, detected profile, and languages.</summary>
    public (string Folder, IFrameworkProfile Profile, IEnumerable<string> Languages)? GetResult()
    {
        if (!IsValid || SelectedProfile == null) return null;
        return (Folder, SelectedProfile, DetectedLanguages.ToList());
    }
}
