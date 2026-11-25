using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;

namespace Toucan.Avalonia.Services;

/// <summary>Opens the system browser for OAuth sign-ins.</summary>
internal sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public Task OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        PlatformService.OpenUrl(url.AbsoluteUri);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs the sign-in method an account uses and returns the saved account, or null when the user cancelled. Secrets are asked
/// for in masked boxes, passed straight to the sign-in service and never kept here.
/// </summary>
internal sealed class SourceSignInFlow(SourceSignInService signIn, IDialogService dialogs, IAsyncMessageService messages)
{
    public async Task<SourceAccount?> RunAsync(RegisteredSourceAdapter adapter, Uri? baseUrl, SourceAuthMethod method, SourceAccount? existing = null,
        CancellationToken cancellationToken = default)
    {
        var id = adapter.Descriptor.Id;
        var service = adapter.Descriptor.DisplayName;
        try
        {
            switch (method)
            {
                case ApiTokenAuth token:
                {
                    if (token.TokenHelpUrl is { } help) PlatformService.OpenUrl(help.AbsoluteUri);
                    var value = await dialogs.ShowSecretPromptAsync($"Sign in to {service}", "Paste your access token.").ConfigureAwait(true);
                    return string.IsNullOrWhiteSpace(value) ? null : await signIn.SignInWithTokenAsync(id, baseUrl, token.Id, value.Trim(), existing: existing).ConfigureAwait(true);
                }
                case BasicAuth basic:
                {
                    var user = await dialogs.ShowPromptAsync($"Sign in to {service}", "User name", existing?.DisplayName ?? string.Empty).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(user)) return null;
                    var password = await dialogs.ShowSecretPromptAsync($"Sign in to {service}", "Password").ConfigureAwait(true);
                    return string.IsNullOrEmpty(password) ? null : await signIn.SignInWithPasswordAsync(id, baseUrl, basic.Id, user.Trim(), password, existing: existing).ConfigureAwait(true);
                }
                case OAuthAuthorizationCodeAuth browser:
                    return await signIn.SignInWithBrowserAsync(id, baseUrl, browser.Id, new SystemBrowserLauncher(), existing: existing, cancellationToken: cancellationToken).ConfigureAwait(true);
                case OAuthDeviceCodeAuth device:
                    return await signIn.SignInWithDeviceCodeAsync(id, baseUrl, device.Id, async prompt =>
                    {
                        PlatformService.OpenUrl((prompt.VerificationUriComplete ?? prompt.VerificationUri).AbsoluteUri);
                        // Not awaited: the message stays up while the sign-in polls, and closes the flow's wait when dismissed.
                        _ = messages.ShowMessageAsync($"Enter the code {prompt.UserCode} at {prompt.VerificationUri} to sign in to {service}. Toucan continues when you finish.", "Sign in");
                        await Task.CompletedTask.ConfigureAwait(true);
                    }, existing: existing, cancellationToken: cancellationToken).ConfigureAwait(true);
                case CustomAuth custom:
                {
                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var field in custom.Fields.Fields)
                    {
                        var value = field.Type == Toucan.Plugins.ConfigFieldType.Secret
                            ? await dialogs.ShowSecretPromptAsync($"Sign in to {service}", field.Label).ConfigureAwait(true)
                            : await dialogs.ShowPromptAsync($"Sign in to {service}", field.Label).ConfigureAwait(true);
                        if (value is null) return null;
                        fields[field.Key] = value;
                    }
                    return await signIn.SignInWithFormAsync(id, baseUrl, custom.Id, fields, existing: existing, cancellationToken: cancellationToken).ConfigureAwait(true);
                }
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is SourceException or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            await messages.ShowMessageAsync(signIn.DiagnosticsFor(adapter).Redact(ex is SourceException s ? s.Error.Message : ex.Message), $"Sign in to {service}").ConfigureAwait(true);
            return null;
        }
    }
}
