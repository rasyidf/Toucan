using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

public partial class NotificationHistoryViewModel : ObservableObject, IDisposable
{
    private readonly INotificationCenter _center;
    private readonly Func<string, Task> _execute;
    private bool _disposed;
    public ObservableCollection<HostNotification> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    public int Count => Items.Count;

    public NotificationHistoryViewModel(INotificationCenter center, Func<string, Task> execute)
    {
        _center = center; _execute = execute;
        Refresh(); _center.Notified += OnNotified;
    }

    private void OnNotified(object? sender, HostNotificationEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        Items.Clear();
        foreach (var item in _center.History.Reverse()) Items.Add(item);
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Count));
    }

    [RelayCommand]
    private void Clear()
    {
        _center.Clear(); Refresh();
    }

    [RelayCommand]
    private async Task RunAction(HostNotification? entry)
    {
        if (entry?.Content.ActionCommandId is { Length: > 0 } id && _center.History.Any(i => i.Id == entry.Id))
            await _execute(id);
    }

    public void Dispose()
    {
        _disposed = true; _center.Notified -= OnNotified;
        GC.SuppressFinalize(this);
    }
}
