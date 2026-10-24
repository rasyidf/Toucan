using Avalonia.Threading;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class TranslationItemViewModel
{
    private readonly Toucan.Avalonia.Services.SpellingSession? _spelling;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "RefreshSpellingAsync owns and disposes the CTS in a using scope. Row disposal cancels it; disposing here races the in-flight operation.")]
    private CancellationTokenSource? _spellingCancellation;
    private bool _spellingDisposed;
    private bool _spellingActive;
    private SpellingResult? _spellingResult;
    public IReadOnlyList<SpellingIssue> SpellingIssues => _spellingResult?.Issues ?? [];
    public string SpellingStatus { get; private set; } = "";
    public string? CheckedSpellingText => _spellingResult?.Text;
    public bool SpellingActive
    {
        get => _spellingActive;
        set { _spellingActive = value; if (value) QueueSpelling(); else { _spellingCancellation?.Cancel(); ClearSpelling(); } }
    }
    private void OnSpellingChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(QueueSpelling);
    private void ClearSpelling()
    { _spellingResult = null; OnPropertyChanged(nameof(SpellingIssues)); }
    private void QueueSpelling()
    {
        _spellingCancellation?.Cancel();
        ClearSpelling();
        if (_spellingDisposed || !_spellingActive || _spelling == null) return;
        _ = RefreshSpellingAsync(true);
    }
    public async Task RefreshSpellingAsync(bool debounce = false)
    {
        if (_spelling == null || _spellingDisposed) return;
        _spellingCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _spellingCancellation = cancellation;
        var expected = Value;
        SpellingStatus = Locales.Loc.T("Checking spelling locally…"); OnPropertyChanged(nameof(SpellingStatus)); OnPropertyChanged(nameof(EditorDetails));
        try
        {
            if (debounce) await Task.Delay(400, cancellation.Token);
            var result = await _spelling.CheckAsync(expected, Language, cancellation.Token);
            if (_spellingDisposed || !_spelling.IsCurrent || cancellation.IsCancellationRequested || Value != expected || !ReferenceEquals(cancellation, _spellingCancellation)) return;
            _spellingResult = result; SpellingStatus = result.Status;
            OnPropertyChanged(nameof(SpellingIssues)); OnPropertyChanged(nameof(SpellingStatus)); OnPropertyChanged(nameof(EditorDetails));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is FormatException or ObjectDisposedException) { SpellingStatus = ex.Message; OnPropertyChanged(nameof(SpellingStatus)); OnPropertyChanged(nameof(EditorDetails)); }
        finally { if (ReferenceEquals(cancellation, _spellingCancellation)) _spellingCancellation = null; }
    }
    public bool ApplySpellingCorrection(SpellingIssue issue, string suggestion)
    {
        if (_spellingDisposed || _spelling?.IsCurrent != true || !_model.Permissions.EditValues || PanelService.Instance.IsAuditMode || CheckedSpellingText != Value || !SpellingIssues.Contains(issue) ||
            !issue.Suggestions.Contains(suggestion) || issue.Offset < 0 || issue.Offset + issue.Length > Value.Length || Value.Substring(issue.Offset, issue.Length) != issue.Word) return false;
        Flush(); // Separate typing and correction into distinct normal history steps.
        Value = Value.Remove(issue.Offset, issue.Length).Insert(issue.Offset, suggestion);
        Flush();
        return true;
    }
    public void IgnoreSpelling(SpellingIssue issue)
    { if (!_spellingDisposed && _spelling?.IsCurrent == true && CheckedSpellingText == Value && SpellingIssues.Contains(issue)) _spelling?.Ignore(issue.Word, Language); }
    public void RememberSpelling(SpellingIssue issue, bool project)
    {
        if (_spellingDisposed || _spelling?.IsCurrent != true || CheckedSpellingText != Value || !SpellingIssues.Contains(issue)) return;
        try { _spelling?.Remember(issue.Word, Language, project); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { SpellingStatus = ex.Message; OnPropertyChanged(nameof(SpellingStatus)); OnPropertyChanged(nameof(EditorDetails)); }
    }
}
