using System;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class TranslationItemViewModel : ObservableObject, IDisposable
{
    private string _value = string.Empty;
    private readonly TranslationItem? _model;
    private Timer _debounceTimer;

    public TranslationItemViewModel()
    {
        // leave empty for design-time or simple instantiation
        _model = null;

        // Set up a timer for 500ms (half a second)
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

                // 1. Stop the timer if it was already running (user kept typing)
                _debounceTimer.Stop();

                // 2. Start the timer again
                _debounceTimer.Start();
            }
        }
    }

    public string Language { get; set; } = string.Empty;

    private void SaveTranslation()
    {
        // This only runs 500ms AFTER the user STOPS typing.
        System.Diagnostics.Debug.WriteLine($"Saving to DB: {_value}");

        // If this VM wraps a model, write the value back to the model
        _model?.Value = _value;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}