using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "CheckProjectSpelling owns and disposes this CTS in its using scope; workspace disposal only cancels the operation.")]
    private CancellationTokenSource? _spellingValidation;
    [ObservableProperty] private bool isCheckingProjectSpelling;
    [RelayCommand] private void CancelSpellingValidation() => _spellingValidation?.Cancel();
    [RelayCommand]
    private async Task CheckProjectSpelling()
    {
        if (!HasProject || _spellingSession == null || IsCheckingProjectSpelling) return;
        FlushPendingEdits(); var project = ProjectSettings; var revision = _workspaceRevision;
        using var cancellation = new CancellationTokenSource(); _spellingValidation = cancellation;
        IsCheckingProjectSpelling = true;
        var pending = new List<ValidationResult>();
        try
        {
            var unavailable = new HashSet<string>();
            foreach (var item in AllTranslation.Take(2000).ToList())
            {
                var result = await _spellingSession.CheckAsync(item.Value, item.Language, cancellation.Token);
                if (!ReferenceEquals(project, ProjectSettings) || revision != _workspaceRevision) { StatusText = Locales.Loc.T("Spelling results discarded because the workspace changed."); return; }
                if (!result.Available)
                {
                    if (unavailable.Add(item.Language)) pending.Add(new("local-spelling-availability", ValidationSeverity.Info, result.Status, Language: item.Language));
                    continue;
                }
                foreach (var issue in result.Issues) pending.Add(new("local-spelling", ValidationSeverity.Warning,
                    $"{issue.Word} ({issue.Offset + 1}): {string.Join(", ", issue.Suggestions)}", item.Namespace, item.Language));
                if (pending.Count >= 2000) break;
            }
            foreach (var old in ValidationIssues.Where(i => i.RuleId is "local-spelling" or "local-spelling-availability").ToList()) ValidationIssues.Remove(old);
            foreach (var finding in pending) ValidationIssues.Add(new ValidationIssueItem(finding.Namespace ?? finding.RuleId,
                $"[{finding.Language}] {finding.Message}", finding.Severity, finding.Namespace, finding.Language, null, finding.RuleId));
            OnValidationIssuesChanged(); SidePanelRegistry.Instance.Activate("issues");
            StatusText = AllTranslation.Count > 2000 || pending.Count >= 2000 ? Locales.Loc.T("Spelling results are partial because the project limit was reached.") : Locales.Loc.T("Local spelling check complete.");
        }
        catch (OperationCanceledException) { StatusText = Locales.Loc.T("Spelling check cancelled."); }
        catch (Exception ex) when (ex is FormatException or ObjectDisposedException) { StatusText = ex.Message; }
        finally { if (ReferenceEquals(_spellingValidation, cancellation)) { _spellingValidation = null; IsCheckingProjectSpelling = false; } }
    }
}
