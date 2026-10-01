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

    private string _value;
    private string _valueBeforeEdit;
    private DispatcherTimer? _debounceTimer;

    public TranslationItemViewModel(
        TranslationItem model,
        IUndoRedoService? undoRedoService = null,
        ITranslationManagementService? translationManagement = null,
        Action<TranslationItemViewModel>? onChanged = null)
    {
        _model = model;
        _undoRedoService = undoRedoService;
        _translationManagement = translationManagement;
        _onChanged = onChanged;
        _value = model.Value ?? string.Empty;
        _valueBeforeEdit = _value;
        Language = model.Language;
    }

    public TranslationItem Model => _model;

    public string Namespace => _model.Namespace;

    public string Language { get; }

    public bool IsRtl => Toucan.Core.RtlHelper.IsRtl(Language);

    public FlowDirection FlowDirection => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _debounceTimer ??= CreateTimer();
            if (!_debounceTimer.IsEnabled) _valueBeforeEdit = _value;
            _value = value;
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
            if (_model.Comment == value) return;
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
            if (_model.IsApproved == value) return;
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
        // Never drop an in-flight edit when the editor list is rebuilt.
        Flush();
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
