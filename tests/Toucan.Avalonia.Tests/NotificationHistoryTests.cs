using Avalonia.Headless.XUnit;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R6: the status-bar notification history reflects INotificationCenter and tracks unseen notifications.</summary>
public class NotificationHistoryTests
{
    private static PluginNotification Note(string title, NotificationSeverity severity = NotificationSeverity.Info) =>
        new() { Title = title, Severity = severity };

    [AvaloniaFact]
    public void StartsFromExistingHistoryWithNothingUnseen()
    {
        var center = new NotificationCenter();
        center.Publish("toucan", Note("before"));

        using var vm = new NotificationHistoryViewModel(center);

        Assert.Single(vm.Entries);
        Assert.Equal("before", vm.Entries[0].Title);
        Assert.Equal(0, vm.UnseenCount); // history that predates the view is already seen
        Assert.False(vm.HasUnseen);
    }

    [AvaloniaFact]
    public void ArrivingNotificationsCountAsUnseenAndAreNewestFirst()
    {
        var center = new NotificationCenter();
        using var vm = new NotificationHistoryViewModel(center);

        center.Publish("toucan", Note("first"));
        center.Publish("plugin.x", Note("second", NotificationSeverity.Error));

        Assert.Equal(2, vm.UnseenCount);
        Assert.True(vm.HasUnseen);
        Assert.Equal("second", vm.Entries[0].Title); // newest first
        Assert.Equal("first", vm.Entries[1].Title);
        Assert.Equal("Error", vm.Entries[0].SeverityLabel);
        Assert.Equal("plugin.x", vm.Entries[0].Source);
    }

    [AvaloniaFact]
    public void MarkSeenClearsTheUnseenCountButKeepsHistory()
    {
        var center = new NotificationCenter();
        using var vm = new NotificationHistoryViewModel(center);
        center.Publish("toucan", Note("a"));
        center.Publish("toucan", Note("b"));
        Assert.Equal(2, vm.UnseenCount);

        vm.MarkSeen();

        Assert.Equal(0, vm.UnseenCount);
        Assert.False(vm.HasUnseen);
        Assert.Equal(2, vm.Entries.Count); // history is retained
    }

    [AvaloniaFact]
    public void SeverityLabelsDistinguishProgressCancellationAndCompletion()
    {
        var center = new NotificationCenter();
        using var vm = new NotificationHistoryViewModel(center);

        center.Publish("op", Note("working", NotificationSeverity.Info));      // progress
        center.Publish("op", Note("stopped", NotificationSeverity.Warning));   // cancellation
        center.Publish("op", Note("finished", NotificationSeverity.Success));  // completion

        Assert.Equal("Done", vm.Entries[0].SeverityLabel);     // finished
        Assert.Equal("Warning", vm.Entries[1].SeverityLabel);  // stopped
        Assert.Equal("Info", vm.Entries[2].SeverityLabel);     // working
    }

    [AvaloniaFact]
    public void ClearEmptiesHistoryAndCenter()
    {
        var center = new NotificationCenter();
        using var vm = new NotificationHistoryViewModel(center);
        center.Publish("toucan", Note("a"));

        vm.Clear();

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Entries);
        Assert.Equal(0, vm.UnseenCount);
        Assert.Empty(center.History);
    }
}
