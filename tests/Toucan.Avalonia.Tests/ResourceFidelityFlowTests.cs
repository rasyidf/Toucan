using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class ResourceFidelityFlowTests
{
    [AvaloniaTheory]
    [InlineData("json", "json", "{\"title\":\"Original\",\"opaque\":null}")]
    [InlineData("yaml", "yml", "title: Original\nopaque: null\n")]
    [InlineData("arb", "arb", "{\"title\":\"Original\",\"@title\":{\"description\":\"Original note\",\"custom\":42}}")]
    [InlineData("java-properties", "properties", "# Keep header\ntitle=Original\n")]
    public async Task ResourceSettingsAndMetadataSurviveGuiSaveAsAndNativeReopen(string format, string extension, string content)
    {
        using var host = new TestHost();
        var root = Directory.CreateDirectory(Path.Combine(host.Root, "fidelity")).FullName;
        var resource = new ResourceDescriptor { Id = "catalog", RelativePath = "custom." + extension, Language = "en", FormatId = format };
        File.WriteAllText(Path.Combine(root, resource.RelativePath), content, new UTF8Encoding(false));
        new ProjectSettings { ProjectPath = root, SaveFormat = format, Languages = ["en"], ResourceDocuments = [new() { Resource = resource }],
            ResourceOptions = new() { InputEncoding = "UTF-8", OutputEncoding = "UTF-8 BOM", Newline = ResourceNewline.CrLf,
                Indent = ResourceIndent.Space4, Order = ResourceOrder.Preserve, SaveEmptyTranslations = false } }.Save();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(root);
        vm.PagingController.Data.Single().Translations.Single().Value = "日本語 — café 😀";
        vm.FlushPendingEdits();
        host.Services.GetRequiredService<ITranslationManagementService>().NotifyCommentChanged(Assert.Single(vm.AllTranslation), "Edited note");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        var target = Path.Combine(host.Root, "fidelity-copy");
        var saved = await host.Services.GetRequiredService<IProjectLifecycleService>().SaveProjectAsAsync(target);
        Assert.Equal(ProjectSaveStatus.Success, saved.Status);
        await vm.OpenProjectAsync(target);
        var back = Assert.Single(vm.AllTranslation);
        Assert.Equal("日本語 — café 😀", back.Value);
        Assert.Equal("Edited note", back.Comment);
        var bytes = File.ReadAllBytes(Path.Combine(target, resource.RelativePath));
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = File.ReadAllText(Path.Combine(target, resource.RelativePath));
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        if (format == "arb")
        {
            using var json = JsonDocument.Parse(text);
            Assert.Equal("Edited note", json.RootElement.GetProperty("@title").GetProperty("description").GetString());
            Assert.Equal(42, json.RootElement.GetProperty("@title").GetProperty("custom").GetInt32());
        }
        if (format == "java-properties") Assert.Contains("# Keep header", text);
    }
}
