using System;
using System.Timers;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class TranslationItemViewModel : ObservableObject, IDisposable
{
    private string _value = string.Empty;
    private readonly TranslationItem? _model;
    private Timer _debounceTimer;

    public TranslationItemViewModel()
    {
        _model = null;
        _debounceTimer = new Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (s, e) => SaveTranslation();
    }

    public TranslationItemViewModel(TranslationItem model) : this()
    {
        _model = model;
        _value = model?.Value ?? string.Empty;
        Language = model?.Language ?? string.Empty;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
    }

    public string Language { get; set; } = string.Empty;

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
    private void ToggleApproved() => IsApproved = !IsApproved;

    [RelayCommand]
    private void CopyTranslation()
    {
        if (!string.IsNullOrEmpty(Value))
            Clipboard.SetText(Value);
    }

    private void SaveTranslation()
    {
        System.Diagnostics.Debug.WriteLine($"Saving to DB: {_value}");
        _model?.Value = _value;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}