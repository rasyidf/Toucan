using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Toucan.Core.Contracts;

namespace Toucan.Avalonia.Services;

/// <summary>Three-way result for Save/Discard/Cancel style prompts.</summary>
public enum ChoiceResult
{
    Primary,
    Secondary,
    Cancel
}

/// <summary>
/// Async message boxes for the Avalonia UI. Avalonia dialogs are asynchronous, so view models
/// use these methods instead of the synchronous <see cref="IMessageService"/> contract.
/// </summary>
public interface IAsyncMessageService : IMessageService
{
    Task ShowMessageAsync(string message, string title = "Info");
    Task<bool> ConfirmAsync(string message, string title = "Confirm", string yesText = "Yes", string noText = "No");
    Task<ChoiceResult> ChooseAsync(string message, string title, string primaryText, string secondaryText, string cancelText = "Cancel");
}

/// <summary>
/// Message boxes built on FluentAvalonia's <see cref="FAContentDialog"/>, which renders as an overlay
/// inside the owning window on every platform.
/// </summary>
public sealed class MessageService : IAsyncMessageService
{
    public void ShowMessage(string message, string title = "Info")
    {
        _ = ShowMessageAsync(message, title);
    }

    /// <summary>
    /// Synchronous confirmation for callers that cannot await. Runs a nested dispatcher frame
    /// until the dialog closes. Prefer <see cref="ConfirmAsync"/>.
    /// </summary>
    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var task = ConfirmAsync(message, title);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.IsCompletedSuccessfully && task.Result;
    }

    public async Task ShowMessageAsync(string message, string title = "Info")
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = BuildContent(message),
            CloseButtonText = "OK",
            DefaultButton = FAContentDialogButton.Close
        };
        await ShowAsync(dialog);
    }

    public async Task<bool> ConfirmAsync(string message, string title = "Confirm", string yesText = "Yes", string noText = "No")
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = BuildContent(message),
            PrimaryButtonText = yesText,
            CloseButtonText = noText,
            DefaultButton = FAContentDialogButton.Primary
        };
        return await ShowAsync(dialog) == FAContentDialogResult.Primary;
    }

    public async Task<ChoiceResult> ChooseAsync(string message, string title, string primaryText, string secondaryText, string cancelText = "Cancel")
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = BuildContent(message),
            PrimaryButtonText = primaryText,
            SecondaryButtonText = secondaryText,
            CloseButtonText = cancelText,
            DefaultButton = FAContentDialogButton.Primary
        };
        return await ShowAsync(dialog) switch
        {
            FAContentDialogResult.Primary => ChoiceResult.Primary,
            FAContentDialogResult.Secondary => ChoiceResult.Secondary,
            _ => ChoiceResult.Cancel
        };
    }

    private static global::Avalonia.Controls.TextBlock BuildContent(string message) => new()
    {
        Text = message,
        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
        MaxWidth = 460
    };

    private static async Task<FAContentDialogResult> ShowAsync(FAContentDialog dialog)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(dialog));
        }

        var owner = AppWindows.Active;
        return owner != null ? await dialog.ShowAsync(owner) : await dialog.ShowAsync();
    }
}
