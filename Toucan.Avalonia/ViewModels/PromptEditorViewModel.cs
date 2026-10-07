using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

/// <summary>A variable as shown next to the prompt editor.</summary>
public sealed record PromptVariableRow(string Token, string Description);

/// <summary>
/// Edits one AI feature's system prompt, either the user's version (all projects) or the open project's version.
/// Shows the variables the prompt can use and the reply format Toucan expects.
/// </summary>
public partial class PromptEditorViewModel : ObservableObject
{
    private readonly IPromptLibrary _prompts;

    public PromptEditorViewModel(IPromptLibrary prompts, AiFeatureDefinition feature, string? projectPath)
    {
        _prompts = prompts;
        Feature = feature;
        ProjectPath = string.IsNullOrEmpty(projectPath) ? null : projectPath;
        // Open the level that is in effect: the project's version if it has one, otherwise the user's.
        projectScope = ProjectPath != null && prompts.GetOverride(feature.Id, PromptSource.Project, ProjectPath) != null;
        Load();
    }

    public AiFeatureDefinition Feature { get; }
    public string Title => $"{Feature.DisplayName} prompt";
    public string? ProjectPath { get; }
    public bool HasProject => ProjectPath != null;
    /// <summary>The variables this prompt can use, written as they go in the prompt (<c>{{context}}</c>).</summary>
    public IReadOnlyList<PromptVariableRow> Variables => [.. Feature.Variables.Select(v => new PromptVariableRow("{{" + v.Name + "}}", v.Description))];

    /// <summary>Editing the project's version (<c>.toucan/prompts</c>) rather than the user's.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScopeLabel), nameof(FilePath))] private bool projectScope;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsDefault))] private string text = string.Empty;

    /// <summary>Where the text in the editor came from when it was loaded.</summary>
    [ObservableProperty] private string originText = string.Empty;

    /// <summary>A version is saved at the level being edited, so "Remove saved version" has something to remove.</summary>
    [ObservableProperty] private bool hasOverride;

    public bool IsDefault => Normalize(Text) == Normalize(Feature.DefaultPrompt);

    public string ScopeLabel => ProjectScope
        ? $"This project only. Saved in the project folder (.toucan/prompts), so it can be committed and shared."
        : "All projects. Saved in your Documents/Toucan/prompts folder.";

    public string FilePath => ProjectScope && ProjectPath != null
        ? Path.Combine(_prompts.ProjectFolder(ProjectPath), Feature.Id + ".md")
        : Path.Combine(_prompts.UserFolder, Feature.Id + ".md");

    public Action<bool>? CloseAction { get; set; }

    partial void OnProjectScopeChanged(bool value) => Load();

    /// <summary>Shows the version saved at the chosen level, or what that level inherits when it has none.</summary>
    private void Load()
    {
        var own = _prompts.GetOverride(Feature.Id, ProjectScope ? PromptSource.Project : PromptSource.User, ProjectPath);
        HasOverride = own != null;
        if (own != null)
        {
            Text = own.Text;
            OriginText = ProjectScope ? "Editing this project's prompt." : "Editing your prompt.";
            return;
        }

        var inherited = ProjectScope ? _prompts.GetPrompt(Feature.Id) : null;
        if (inherited is { Source: PromptSource.User })
        {
            Text = inherited.Text;
            OriginText = "This project uses your prompt. Saving creates a copy for this project.";
        }
        else
        {
            Text = Feature.DefaultPrompt;
            OriginText = "Showing the built-in prompt. Saving keeps your changes as your own version.";
        }
    }

    [RelayCommand] private void UseAllProjects() => ProjectScope = false;
    [RelayCommand] private void UseThisProject() => ProjectScope = true;

    [RelayCommand]
    private void ResetToDefault() => Text = Feature.DefaultPrompt;

    [RelayCommand]
    private void Save()
    {
        var scope = ProjectScope ? PromptSource.Project : PromptSource.User;
        // A project prompt equal to the default is still saved: it pins the default for this project.
        _prompts.Save(Feature.Id, Text, scope, ProjectPath);
        CloseAction?.Invoke(true);
    }

    /// <summary>Removes the saved version at this level, so the next level down is used again.</summary>
    [RelayCommand]
    private void RemoveOverride()
    {
        _prompts.Reset(Feature.Id, ProjectScope ? PromptSource.Project : PromptSource.User, ProjectPath);
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        PlatformService.RevealInFileManager(folder);
    }

    private static string Normalize(string value) => value.ReplaceLineEndings("\n").Trim();
}
