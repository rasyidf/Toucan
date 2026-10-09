using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>The status bar's view of background work: how much is running, what it says, and each operation to cancel.</summary>
public sealed partial class OperationsViewModel : ObservableObject
{
    private readonly IBackgroundOperationService? _service;

    public OperationsViewModel(IBackgroundOperationService? service = null)
    {
        _service = service;
        if (service is not null)
        {
            service.Changed += (_, _) => OnUi(Refresh);
            Refresh();
        }
    }

    public ObservableCollection<BackgroundOperation> Items { get; } = [];

    [ObservableProperty] private bool hasActive;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string toolTip = string.Empty;

    private void Refresh()
    {
        var active = _service?.Active ?? [];
        foreach (var gone in Items.Where(i => !active.Contains(i)).ToList())
        {
            gone.PropertyChanged -= OnOperationChanged;
            Items.Remove(gone);
        }
        foreach (var added in active.Where(a => !Items.Contains(a)))
        {
            added.PropertyChanged += OnOperationChanged;
            Items.Add(added);
        }
        Update();
    }

    private void OnOperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnUi(Update);

    private void Update()
    {
        HasActive = Items.Count > 0;
        Summary = Items.Count switch
        {
            0 => string.Empty,
            1 => Describe(Items[0], withPercent: false),
            _ => Locales.Loc.Format("{0} tasks running", Items.Count),
        };
        ToolTip = string.Join(Environment.NewLine, Items.Select(i => Describe(i, withPercent: true)));
    }

    /// <summary>"Title: message (40%)", leaving out what is not known.</summary>
    public static string Describe(BackgroundOperation operation, bool withPercent)
    {
        var text = string.IsNullOrEmpty(operation.Message) ? operation.Title : $"{operation.Title}: {operation.Message}";
        return withPercent && operation.Fraction is { } f ? $"{text} ({f:P0})" : text;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
