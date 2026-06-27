using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Windows.Threading;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class TranslationItemViewModel : ObservableObject
{
    private string _value = string.Empty;
    private string _valueBeforeEdit = string.Empty;
    private readonly TranslationItem? _model;
    private readonly DispatcherTimer _debounceTimer;

    public TranslationItemViewModel()
    {
        _model = null;
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += (s, e) => { _debounceTimer.Stop(); SaveTranslation(); };
    }

    public TranslationItemViewModel(TranslationItem model) : this()
    {
        _model = model;
        _value = model?.Value ?? string.Empty;
        _valueBeforeEdit = _value;
        Language = model?.Language ?? string.Empty;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                if (!_debounceTimer.IsEnabled)
                {
                    _valueBeforeEdit = _value;
                }

                _value = value;
                OnPropertyChanged(nameof(Value));
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
    }

    public string Language { get; set; } = string.Empty;

    public string FontFamily => Toucan.Core.RtlHelper.GetFontFamily(Language);

    public FlowDirection FlowDirection =>
        Toucan.Core.RtlHelper.IsRtl(Language) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string Comment
    {
        get => _model?.Comment ?? string.Empty;
        set { if (_model != null) { _model.Comment = value; OnPropertyChanged(nameof(Comment)); } }
    }

    public bool IsApproved
    {
        get => _model?.IsApproved ?? false;
        set { if (_model != null) { _model.IsApproved = value; OnPropertyChanged(nameof(IsApproved)); } }
    }

    [RelayCommand]
    private void ToggleApproved()
    {
        IsApproved = !IsApproved;
    }

    [RelayCommand]
    private void CopyTranslation()
    {
        if (!string.IsNullOrEmpty(Value))
        {
            Clipboard.SetText(Value);
        }
    }

    private void SaveTranslation()
    {
        if (_model != null && _valueBeforeEdit != _value)
        {
            Services.UndoRedoService.Instance.Record(_model.Namespace, _model.Language, _valueBeforeEdit, _value);
        }

        _ = _model?.Value = _value;
        _valueBeforeEdit = _value;
    }
}
