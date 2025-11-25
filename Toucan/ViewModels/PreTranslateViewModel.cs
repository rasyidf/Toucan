using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class LanguageItem : ObservableObject
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private bool isSelected;

    public LanguageItem(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

public partial class PreTranslateViewModel : ObservableObject
{
    public static Func<string, object, bool> StringEqualsConverter = (param, value) => string.Equals((string?)param, (string?)value, StringComparison.InvariantCultureIgnoreCase);

    [ObservableProperty]
    private string selectedProvider = "Google";

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } = new();

    [ObservableProperty]
    private bool resetApproved = true;

    [ObservableProperty]
    private bool translateSelectedOnly = true;

    [ObservableProperty]
    private bool overwriteExisting = false;

    private readonly IPretranslationService? _pretranslationService;
    private readonly IEnumerable<TranslationItem>? _sourceItems;

    private System.Threading.CancellationTokenSource? _cts;

    public ObservableCollection<PretranslationItemResult> PreviewResults { get; } = new();

    [ObservableProperty]
    private int progressCompleted;

    [ObservableProperty]
    private int progressTotal;

    // ProgressBar.Value expects a double. Return double here to avoid conversion issues
    // and ensure the value is clamped to the 0..100 range.
    public double ProgressPercent => progressTotal == 0 ? 0.0 : Math.Clamp(((double)progressCompleted / progressTotal * 100.0), 0.0, 100.0);

    [ObservableProperty]
    private bool isRunning;

    public PreTranslateViewModel()
    {
        // default languages for preview
        AvailableLanguages.Add(new LanguageItem("id-ID", true));
        AvailableLanguages.Add(new LanguageItem("zh-CN", true));
        AvailableLanguages.Add(new LanguageItem("fr-FR", false));
    }

    public PreTranslateViewModel(IEnumerable<string> languages, IEnumerable<TranslationItem>? sourceItems = null, IPretranslationService? pretranslation = null)
    {
        _pretranslationService = pretranslation;
        _sourceItems = sourceItems;

        foreach (var l in languages)
            AvailableLanguages.Add(new LanguageItem(l, true));
    }

    // Cancel command for running preview will be provided below (cancellation-token aware).

    [RelayCommand]
    private async Task Start()
    {
        // Gather request and call pretranslation service if available
        if (_pretranslationService == null || _sourceItems == null)
        {
            // Nothing to do in this simplified flow
            return;
        }

        var selectedLanguages = AvailableLanguages.Where(l => l.IsSelected).Select(l => l.Name).ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        IEnumerable<TranslationItem> itemsToProcess = _sourceItems;
        if (TranslateSelectedOnly)
            itemsToProcess = itemsToProcess.Where(i => selectedLanguages.Contains(i.Language));

        PreviewResults.Clear();

        var req = new PretranslationRequest
        {
            Provider = SelectedProvider,
            Items = itemsToProcess.ToList(),
            ContextItems = _sourceItems,
            Options = new PretranslationOptions { Overwrite = OverwriteExisting, PreviewOnly = true }
        };

        if (_pretranslationService == null) return;

        _cts = new System.Threading.CancellationTokenSource();
        IsRunning = true;
        ProgressCompleted = 0;
        ProgressTotal = 0;

            var progress = new Progress<PretranslationProgress>(p =>
            {
                // Always marshal updates to the UI thread when available to avoid binding exceptions
                try
                {
                    Action update = () =>
                    {
                        ProgressCompleted = p.Completed;
                        ProgressTotal = p.Total;
                        OnPropertyChanged(nameof(ProgressPercent));
                    };

                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher?.CheckAccess() == true)
                    {
                        update();
                    }
                    else if (app?.Dispatcher != null)
                    {
                        // Use Invoke so the update runs immediately. BeginInvoke may never execute
                        // in environments without a running dispatcher loop (unit tests).
                        app.Dispatcher.Invoke(update);
                    }
                    else
                    {
                        // No dispatcher (unit tests or non-WPF host) — just run directly
                        update();
                    }
                }
                catch
                {
                    // Swallow any exceptions from progress updates to ensure we don't bubble an exception back
                    // into providers or the UI binding engine (which would surface as RangeBase.Value exceptions).
                }
            });

        var result = await _pretranslationService.PreTranslateAsync(req, progress, _cts.Token).ConfigureAwait(true);

        // populate preview results on the UI thread
        PreviewResults.Clear();
        foreach (var r in result.Items)
        {
            PreviewResults.Add(r);
        }

        IsRunning = false;
    }

        [RelayCommand]
        private void OpenProviderSettings()
        {
            // We defer construction of any WPF Window until we know we are in a UI host or
            // have a dialog service available. Constructing a Window in headless test runs
            // will throw (needs STA), so avoid building it prematurely.

            // try to use the dialog manager if available
            Toucan.Services.IDialogService? ds = null;
            if (Toucan.App.Services != null)
            {
                ds = Toucan.App.Services.GetService(typeof(Toucan.Services.IDialogService)) as Toucan.Services.IDialogService;
            }

            if (ds != null)
            {
                var window = new Views.Dialogs.ProviderSettingsWindow();
                ds.ShowDialog(window);
                return;
            }

            // fall back to direct modal show. Be defensive: Application.Current (or MainWindow)
            // might be null in unit tests or non-WPF hosts — avoid dereferencing it.
            var app = System.Windows.Application.Current;
            // If there is no Application.Current and App.Services isn't available, we're likely in
            // a headless unit-test environment — do not construct/show the UI window to avoid
            // raising NullReferenceExceptions.
            if (app == null)
                return;

            if (app.MainWindow != null)
            {
                var window = new Views.Dialogs.ProviderSettingsWindow();
                window.Owner = app.MainWindow;

                try
                {
                    window.ShowDialog();
                }
                catch
                {
                    // Swallow exceptions here to avoid crashing headless unit tests / CI runs.
                }
            }
        }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
            _cts.Cancel();
    }

    [RelayCommand]
    private Task Commit()
    {
        // Apply preview results to source items directly (so we don't re-run the provider)
        if (PreviewResults == null || !_sourceItems?.Any() == true)
            return Task.CompletedTask;

        var items = _sourceItems!.ToList();

        foreach (var pr in PreviewResults)
        {
            if (string.IsNullOrEmpty(pr.TranslatedValue)) continue;
            var item = items.FirstOrDefault(i => (i.Namespace ?? string.Empty) == pr.Namespace && i.Language == pr.Language);
            if (item == null) continue;

            // honor overwrite
            if (OverwriteExisting || string.IsNullOrEmpty(item.Value))
            {
                item.Value = pr.TranslatedValue;
            }
        }

        return Task.CompletedTask;
    }
}
