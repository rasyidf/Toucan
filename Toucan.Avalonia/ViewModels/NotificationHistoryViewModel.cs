using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

public partial class NotificationHistoryViewModel : ObservableObject, IDisposable
{
    private readonly INotificationCenter _center;
    private readonly Func<string, Task> _execute;
    private bool _disposed;
    public ObservableCollection<HostNotification> Items { get; } = [];
    public ObservableCollection<NotificationEntryViewModel> Entries { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnseen))] private int unseenCount;
    public bool HasUnseen => UnseenCount > 0;
    public void MarkSeen() => UnseenCount = 0;
    public bool IsEmpty => Items.Count == 0;
    public int Count => Items.Count;

    public NotificationHistoryViewModel(INotificationCenter? center = null, Func<string, Task>? execute = null)
    {
        _center = center ?? new NotificationCenter(); _execute = execute ?? (_ => Task.CompletedTask);
        Refresh(); _center.Notified += OnNotified;
    }

    private void OnNotified(object? sender, HostNotificationEventArgs e)
    {
        void Add() { if (_disposed) return; Refresh(); UnseenCount++; }
        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    private void Refresh()
    {
        if (_disposed) return;
        Items.Clear(); Entries.Clear();
        foreach (var item in _center.History.Reverse()) { Items.Add(item); Entries.Add(new NotificationEntryViewModel(item)); }
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Count));
    }

    [RelayCommand]
    public void Clear()
    {
        _center.Clear(); Refresh(); MarkSeen();
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

/// <summary>One row in the notification history.</summary>
public sealed class NotificationEntryViewModel(HostNotification notification)
{
    public string Title => notification.Content.Title;
    public string? Message => notification.Content.Message;
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public string Source => notification.Source;
    public string Time => notification.Time.LocalDateTime.ToString("HH:mm:ss");
    public NotificationSeverity Severity => notification.Content.Severity;

    /// <summary>Short word for the state, so progress / cancellation / completion read differently in the list.</summary>
    public string SeverityLabel => Severity switch
    {
        NotificationSeverity.Success => "Done",
        NotificationSeverity.Warning => "Warning",
        NotificationSeverity.Error => "Error",
        _ => "Info",
    };

    /// <summary>CSS-like class name the row border uses to color by severity (matches the status-bar badge palette).</summary>
    public string SeverityClass => Severity switch
    {
        NotificationSeverity.Success => "good",
        NotificationSeverity.Warning => "warning",
        NotificationSeverity.Error => "error",
        _ => "info",
    };
}
