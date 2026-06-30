using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>Source control panel: branch name, dirty file count, change summary.</summary>
internal partial class StatusBarViewModel
{
    [ObservableProperty]
    private bool isGitVisible;

    [ObservableProperty]
    private string branchName = "";

    [ObservableProperty]
    private int gitChanges;

    /// <summary>Quick summary lines shown in the source-control popover card.</summary>
    public ObservableCollection<string> ChangesSummary { get; } = [];

    public void UpdateSourceControl(string branch, int changes, IEnumerable<string>? summary = null)
    {
        BranchName = branch;
        GitChanges = changes;
        IsGitVisible = !string.IsNullOrEmpty(branch);

        ChangesSummary.Clear();
        if (summary is not null)
        {
            foreach (var line in summary)
                ChangesSummary.Add(line);
        }
    }
}
