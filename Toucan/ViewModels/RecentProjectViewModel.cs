

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Windows.Input;
using Toucan.Core.Models;
namespace Toucan.ViewModels;

public partial class RecentProjectViewModel : ObservableObject
{
    public string FilePath { get; }
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);

    public ICommand OpenProjectCommand { get; }

    public RecentProjectViewModel(Project project, Action<string> openAction)
    {
        FilePath = project.Path;
        OpenProjectCommand = new RelayCommand(() => openAction?.Invoke(FilePath));
    }
}
