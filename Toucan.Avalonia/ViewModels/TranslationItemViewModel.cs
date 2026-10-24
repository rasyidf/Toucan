using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// Editable view of a single <see cref="TranslationItem"/> (one key in one language).
/// Value edits are debounced (500 ms) before they are recorded for undo and pushed to the
/// translation store, so typing doesn't flood the dirty tracker.
/// </summary>
public partial class TranslationItemViewModel : ObservableObject, IDisposable
{
    private readonly TranslationItem _model;
    private readonly IUndoRedoService? _undoRedoService;
    private readonly ITranslationManagementService? _translationManagement;
    private readonly Action<TranslationItemViewModel>? _onChanged;
    private readonly Func<TranslationItem, bool>? _canApprove;

    private string _value;
    private string _valueBeforeEdit;
    private DispatcherTimer? _debounceTimer;

    public TranslationItemViewModel(
        TranslationItem model,
        IUndoRedoService? undoRedoService = null,
        ITranslationManagementService? translationManagement = null,
        Action<TranslationItemViewModel>? onChanged = null,
        Func<TranslationItem, bool>? canApprove = null, SpellingSession? spelling = null)
    {
        _spelling = spelling;
        if (_spelling != null) _spelling.Changed += OnSpellingChanged;
        _canApprove = canApprove;
        _model = model;
        _undoRedoService = undoRedoService;
        _translationManagement = translationManagement;
        _onChanged = onChanged;
        _value = model.Value ?? string.Empty;
        _valueBeforeEdit = _value;
        Language = model.Language;
    }

    public TranslationItem Model => _model;

    public bool IsResourceReadOnly => !_model.Permissions.EditValues;
    public string ReadOnlyReason => _model.Permissions.Reason ?? Locales.Loc.T("This resource is read-only.");
    public string ResourcePath { get; init; } = "";
    public string EditorDetails => IdentityDetails + (string.IsNullOrEmpty(SpellingStatus) ? "" : "\n" + SpellingStatus);
    public string IdentityDetails => _model.Identity is { } identity ? $"{ResourcePath}\n{identity.Package} / {identity.ResourceId}\n{System.Text.Json.JsonSerializer.Serialize(identity.Path)}" : Namespace;
    public bool IsArbResource { get; init; }
    public bool HasArbMetadata => IsArbResource || _model.FormatData?.ContainsKey("arb.file") == true;
    public string PlaceholderMetadata => _model.FormatData?.GetValueOrDefault("arb.placeholders") ?? "{}";
    public string? ApplyPlaceholderMetadata(string json, string expected)
    {
        if (_spellingDisposed || _spelling?.IsCurrent == false || !_model.Permissions.EditMetadata || PanelService.Instance.IsAuditMode) return Locales.Loc.T("Resource metadata is read-only.");
        if (PlaceholderMetadata != expected) return Locales.Loc.T("Metadata changed. Reopen the editor.");
        try
        {
            Toucan.Core.Services.ArbPlaceholderMetadata.Validate(json);
            var before = new Dictionary<string, string>(_model.FormatData ?? []);
            var after = new Dictionary<string, string>(before) { ["arb.placeholders"] = json };
            _undoRedoService?.RecordMetadata(Namespace, Language, before, after);
            _model.FormatData = after;
            _translationManagement?.NotifyValueChanged(_model, _model.Value);
            _onChanged?.Invoke(this);
            OnPropertyChanged(nameof(PlaceholderMetadata));
            return null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return ex.Message; }
    }
    public string Namespace => _model.Namespace;

    public string Language { get; }

    public bool IsRtl => Toucan.Core.RtlHelper.IsRtl(Language);

    public FlowDirection FlowDirection => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string Value
    {
        get => _value;
        set
        {
            if (!_model.Permissions.EditValues || _value == value) return;
            _debounceTimer ??= CreateTimer();
            if (!_debounceTimer.IsEnabled) _valueBeforeEdit = _value;
            _value = value;
            QueueSpelling();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public string Comment
    {
        get => _model.Comment ?? string.Empty;
        set
        {
            if (!_model.Permissions.EditMetadata || _model.Comment == value) return;
            if (_translationManagement != null)
                _translationManagement.NotifyCommentChanged(_model, value);
            else
                _model.Comment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasComment));
            _onChanged?.Invoke(this);
        }
    }

    public bool HasComment => !string.IsNullOrEmpty(Comment);

    public bool IsApproved
    {
        get => _model.IsApproved;
        set
        {
            if (!_model.Permissions.EditMetadata || _model.IsApproved == value) return;
            if (value && _canApprove?.Invoke(_model) == false)
            {
                // Policy refused: tell bound controls to show the unchanged state again.
                OnPropertyChanged();
                return;
            }
            _model.IsApproved = value;
            _model.ApprovedAtUtc = value ? DateTime.UtcNow : null;
            OnPropertyChanged();
            _onChanged?.Invoke(this);
        }
    }

    /// <summary>True when the value came from machine translation and hasn't been edited since.</summary>
    public bool IsMachineTranslated => _model.ChangeType == ChangeType.Suggestion;

    /// <summary>Controls row visibility for the language filter.</summary>
    [ObservableProperty]
    private bool isLanguageVisible = true;

    /// <summary>Whether the comment flyout is open for this item.</summary>
    [ObservableProperty]
    private bool isCommentOpen;

    /// <summary>Translation memory suggestion shown as ghost text while this field is focused and empty; Tab accepts it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGhostText), nameof(PlaceholderText))]
    private string? ghostText;

    public bool HasGhostText => !string.IsNullOrEmpty(GhostText);

    /// <summary>Hidden while ghost text is shown so the two do not overlap.</summary>
    public string PlaceholderText => HasGhostText ? string.Empty : Locales.Loc.T("Not translated");

    [RelayCommand]
    private void ToggleApproved() => IsApproved = !IsApproved;

    [RelayCommand]
    private void ClearApproved() => IsApproved = false;

    [RelayCommand]
    private void ToggleCommentPopup() => IsCommentOpen = !IsCommentOpen;

    [RelayCommand]
    private Task CopyTranslation() => PlatformService.SetClipboardTextAsync(Value);

    /// <summary>Pushes a pending (debounced) edit to the model immediately, e.g. before saving.</summary>
    public void Flush()
    {
        if (_debounceTimer is { IsEnabled: true })
        {
            _debounceTimer.Stop();
            Commit();
        }
    }

    /// <summary>Re-reads the model after an external change (undo/redo, bulk operation).</summary>
    public void Refresh()
    {
        _debounceTimer?.Stop();
        _value = _model.Value ?? string.Empty;
        _valueBeforeEdit = _value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsApproved));
        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(PlaceholderMetadata));
        QueueSpelling();
    }

    private void Commit()
    {
        if (_valueBeforeEdit != _value)
        {
            _undoRedoService?.Record(_model.Namespace, _model.Language, _valueBeforeEdit, _value);
            _model.LastModifiedUtc = DateTime.UtcNow;
            _model.ChangeType = ChangeType.DirectEdit;
        }

        if (_translationManagement != null)
            _translationManagement.NotifyValueChanged(_model, _value);
        else
            _model.Value = _value;

        _valueBeforeEdit = _value;
        _onChanged?.Invoke(this);
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Commit();
        };
        return timer;
    }

    public void Dispose()
    {
        if (_spellingDisposed) return;
        // Never drop an in-flight edit when the editor list is rebuilt.
        Flush();
        _spellingDisposed = true;
        _spellingCancellation?.Cancel();
        _spellingCancellation = null;
        if (_spelling != null) _spelling.Changed -= OnSpellingChanged;
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
