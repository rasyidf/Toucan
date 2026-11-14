using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Contracts;

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
    private readonly Func<TranslationItemViewModel, IReadOnlyList<ValidationResult>>? _validateInline;

    private string _value;
    private string _valueBeforeEdit;
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _validationTimer;

    public TranslationItemViewModel(
        TranslationItem model,
        IUndoRedoService? undoRedoService = null,
        ITranslationManagementService? translationManagement = null,
        Action<TranslationItemViewModel>? onChanged = null,
        Func<TranslationItem, bool>? canApprove = null, SpellingSession? spelling = null,
        Func<TranslationItemViewModel, IReadOnlyList<ValidationResult>>? validateInline = null)
    {
        _spelling = spelling;
        if (_spelling != null) _spelling.Changed += OnSpellingChanged;
        _validateInline = validateInline;
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
    public bool IsSource { get; init; }
    public int CharacterCount => _value.EnumerateRunes().Count();
    public int WordCount => _value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
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
            QueueInlineValidation();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CharacterCount));
            OnPropertyChanged(nameof(WordCount));
            OnPropertyChanged(nameof(CountSummary));
            OnPropertyChanged(nameof(ShowCount));
            OnPropertyChanged(nameof(MissingPlaceholders));
            OnPropertyChanged(nameof(HasPlaceholderWarning));
            OnPropertyChanged(nameof(PlaceholderWarning));
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>"12 chars · 3 words", for the counter shown under the field.</summary>
    public string CountSummary => $"{CharacterCount} {(CharacterCount == 1 ? "char" : "chars")} · {WordCount} {(WordCount == 1 ? "word" : "words")}";

    /// <summary>True while this field has keyboard focus; the owning card sets it. Drives <see cref="ShowCount"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCount))]
    private bool isFocused;

    /// <summary>The character/word count shows only for the field being edited, so it is not repeated on every row.</summary>
    public bool ShowCount => IsFocused && !IsEmpty;

    /// <summary>
    /// The primary-language value of the same key, for the side-by-side source column. Set by the owning group;
    /// null for the primary-language row itself (it is the source).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource), nameof(MissingPlaceholders), nameof(HasPlaceholderWarning), nameof(PlaceholderWarning))]
    private string? sourceText;

    public bool HasSource => SourceText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInlineIssues), nameof(HasInlineErrors))]
    private IReadOnlyList<ValidationResult> inlineIssues = [];

    public bool HasInlineIssues => InlineIssues.Count > 0;
    public bool HasInlineErrors => InlineIssues.Any(i => i.Severity == ValidationSeverity.Error);

    partial void OnSourceTextChanged(string? value) => QueueInlineValidation();

    private void QueueInlineValidation()
    {
        if (_validateInline is null) return;
        if (_validationTimer is null)
        {
            _validationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _validationTimer.Tick += OnValidationTick;
        }
        _validationTimer.Stop();
        _validationTimer.Start();
    }

    private void OnValidationTick(object? sender, EventArgs e) => RefreshInlineValidation();

    public void RefreshInlineValidation()
    {
        _validationTimer?.Stop();
        InlineIssues = _validateInline?.Invoke(this) ?? [];
    }

    /// <summary>
    /// The <c>{{ }}</c> placeholders present in the source but missing from this value. Empty for the source row,
    /// for an empty value, or when there is no source. Independent of whether the warning is shown.
    /// </summary>
    public IReadOnlyList<string> MissingPlaceholders =>
        SourceText is null || string.IsNullOrEmpty(_value)
            ? []
            : [.. Toucan.Core.Services.PlaceholderService.NamedDouble(SourceText)
                .Where(p => !_value.Contains(p, StringComparison.Ordinal))];

    /// <summary>True when the inline warning should show: the toggle is on and this value drops a source placeholder.</summary>
    public bool HasPlaceholderWarning =>
        Services.PanelService.Instance.ShowPlaceholderWarnings && MissingPlaceholders.Count > 0;

    /// <summary>The warning text listing the missing placeholders, e.g. "Missing: {{name}}, {{count}}".</summary>
    public string PlaceholderWarning =>
        MissingPlaceholders.Count == 0 ? string.Empty : "Missing: " + string.Join(", ", MissingPlaceholders);

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
            Flush();
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
        var incoming = _model.Value ?? string.Empty;
        var valueChanged = _value != incoming;
        _value = incoming;
        _valueBeforeEdit = _value;
        // Only raise Value when it actually changed, so a refresh triggered by an unrelated edit
        // does not reset the caret of a field the user is typing in.
        if (valueChanged)
        {
            QueueInlineValidation();
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CharacterCount));
            OnPropertyChanged(nameof(WordCount));
            OnPropertyChanged(nameof(CountSummary));
            OnPropertyChanged(nameof(ShowCount));
            OnPropertyChanged(nameof(MissingPlaceholders));
            OnPropertyChanged(nameof(HasPlaceholderWarning));
            OnPropertyChanged(nameof(PlaceholderWarning));
        }
        OnPropertyChanged(nameof(IsApproved));
        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(PlaceholderMetadata));
        QueueSpelling();
    }

    /// <summary>Re-evaluates the inline placeholder warning (e.g. after the global toggle changed).</summary>
    public void RefreshPlaceholderWarning()
    {
        OnPropertyChanged(nameof(HasPlaceholderWarning));
        RefreshInlineValidation();
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
        _validationTimer?.Stop();
        if (_validationTimer is not null) _validationTimer.Tick -= OnValidationTick;
        _validationTimer = null;
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
