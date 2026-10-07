using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class StatusBarActionTests
{
    [AvaloniaTheory]
    [InlineData("UTF-8", "LF")]
    [InlineData("UTF-8 BOM", "CRLF")]
    public async Task FileFormatChoices_AreAppliedOnSaveAndPersisted(string encoding, string lineEnding)
    {
        using var host = new TestHost();
        var status = host.Services.GetRequiredService<StatusBarViewModel>();
        StatusBarService.Instance.Register(status);
        try
        {
            var folder = host.CreateJsonProject("format", ("en", """{"first": "Café", "second": "Hello"}"""));
            var vm = host.CreateViewModel();
            await vm.OpenProjectAsync(folder);
            vm.SetTextEncoding(encoding);
            vm.SetLineEnding(lineEnding);
            Assert.True(vm.IsDirty);
            Assert.Equal(encoding, status.Encoding.Encoding);
            Assert.Equal(lineEnding, status.LineEndings.LineEnding);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            var bytes = File.ReadAllBytes(Path.Combine(folder, "en.json"));
            Assert.Equal(encoding == "UTF-8 BOM", bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            var text = File.ReadAllText(Path.Combine(folder, "en.json"));
            if (lineEnding == "LF")
            {
                Assert.Contains("\n", text);
                Assert.DoesNotContain("\r", text);
            }
            else
            {
                Assert.Contains("\r\n", text);
                Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
            }
            var settings = ProjectSettings.LoadFrom(folder)!;
            Assert.Equal(encoding, settings.TextEncoding);
            Assert.Equal(lineEnding, settings.LineEnding);
            await vm.OpenProjectAsync(folder);
            Assert.Equal(encoding, status.Encoding.Encoding);
            Assert.Equal(lineEnding, status.LineEndings.LineEnding);
        }
        finally { StatusBarService.Instance.Unregister(); }
    }

    [AvaloniaFact]
    public async Task Notifications_IncludeValidationAndUntranslatedEntries()
    {
        using var host = new TestHost();
        var status = host.Services.GetRequiredService<StatusBarViewModel>();
        StatusBarService.Instance.Register(status);
        try
        {
            var folder = host.CreateJsonProject("notifications", ("en", """{"title": "Hello"}"""), ("fr", """{"title": ""}"""));
            var vm = host.CreateViewModel();
            await vm.OpenProjectAsync(folder);
            vm.ValidationIssues.Add(new ValidationIssueItem("title", "Bad placeholder", ValidationSeverity.Error));
            vm.ValidationIssues.Add(new ValidationIssueItem("title", "Check punctuation", ValidationSeverity.Warning));
            vm.ValidationIssues.Add(new ValidationIssueItem("title", "Suggestion", ValidationSeverity.Info));
            vm.UpdateSummaryInfo();
            Assert.Equal(1, status.Notifications.Untranslated);
            Assert.Equal(1, status.Notifications.Errors);
            Assert.Equal(1, status.Notifications.Warnings);
            Assert.Equal(1, status.Notifications.Info);
            Assert.Equal(4, status.Notifications.Count);
            vm.ValidationIssues.Clear();
            vm.AllTranslation.Single(t => t.Language == "fr").Value = "Bonjour";
            vm.UpdateSummaryInfo();
            Assert.Equal(0, status.Notifications.Count);
            Assert.True(status.Notifications.IsVisible);
        }
        finally { StatusBarService.Instance.Unregister(); }
    }
}
