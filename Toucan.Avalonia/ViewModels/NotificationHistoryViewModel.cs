using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// The notification history shown from the status-bar badge. Backed by <see cref="INotificationCenter"/>: it keeps the
/// badge's unseen count in step with arriving notifications and exposes the history newest-first for the flyout.
/// </summary>
public partial class NotificationHistoryViewModel : ObservableObject, IDisposable
{
    private readonly INotificationCenter? _center;

    public NotificationHistoryViewModel(INotificationCenter? center = null)
    {
        _center = center;
        if (_center is null) return;
        foreach (var n in _center.History) Entries.Add(new NotificationEntryViewModel(n));
        _center.Notified += OnNotified;
    }

    /// <summary>History newest-first.</summary>
    public ObservableCollection<NotificationEntryViewModel> Entries { get; } = [];

    /// <summary>Notifications that arrived since the history was last opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseen))]
    private int unseenCount;

    public bool HasUnseen => UnseenCount > 0;

    public bool IsEmpty => Entries.Count == 0;

    private void OnNotified(object? sender, HostNotificationEventArgs e)
    {
        void Add()
        {
            Entries.Insert(0, new NotificationEntryViewModel(e.Notification));
            UnseenCount++;
            OnPropertyChanged(nameof(IsEmpty));
        }
        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    /// <summary>Call when the flyout opens: the badge count is cleared.</summary>
    public void MarkSeen() => UnseenCount = 0;

    public void Clear()
    {
        _center?.Clear();
        Entries.Clear();
        UnseenCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        if (_center is not null) _center.Notified -= OnNotified;
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
