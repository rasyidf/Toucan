using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Windows.Input;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class RecentProjectViewModel : ObservableObject
{
    private readonly Action<string>? _removeAction;
    private readonly Action<string>? _pinAction;

    public string FilePath { get; }
    public string DisplayName => Path.GetFileName(FilePath) ?? FilePath;
    public string LastOpened { get; }

    [ObservableProperty]
    private bool isPinned;

    public ICommand OpenProjectCommand { get; }

    public RecentProjectViewModel(Project project, Action<string> openAction, Action<string>? removeAction = null, Action<string>? pinAction = null)
    {
        FilePath = project.Path;
        LastOpened = FormatRelativeTime(project.LastOpened);
        OpenProjectCommand = new RelayCommand(() => openAction?.Invoke(FilePath));
        _removeAction = removeAction;
        _pinAction = pinAction;
    }

    [RelayCommand]
    private void Pin()
    {
        IsPinned = !IsPinned;
        _pinAction?.Invoke(FilePath);
    }

    [RelayCommand]
    private void Remove()
    {
        _removeAction?.Invoke(FilePath);
    }

    private static string FormatRelativeTime(DateTime dateTime)
    {
        if (dateTime == default)
            return string.Empty;

        var elapsed = DateTime.Now - dateTime;

        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "yesterday";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays} days ago";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)} months ago";
        return $"{(int)(elapsed.TotalDays / 365)} years ago";
    }
}
