using System;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class TranslationItemViewModel : ObservableObject, IDisposable
{
    private string _value = string.Empty;
    private readonly TranslationItem? _model;
    private readonly Timer _debounceTimer;

    public TranslationItemViewModel(TranslationItem model)
    {
        _model = model;
        _value = model?.Value ?? string.Empty;
        Language = model?.Language ?? string.Empty;
        _debounceTimer = new Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => { if (_model != null) _model.Value = _value; };
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public string Language { get; set; } = string.Empty;

    public void Dispose() => _debounceTimer?.Dispose();
}
