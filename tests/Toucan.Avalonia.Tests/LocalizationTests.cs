using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Dialogs;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>The id-ID translation table: lookups, completeness against the UI source, and rendering in Indonesian.</summary>
public sealed partial class LocalizationTests
{
    /// <summary>Text that is intentionally the same in every language (names, code, separators).</summary>
    private static readonly HashSet<string> Untranslated =
    [
        "Toucan", "GitHub", "UPPERCASE", "/path/to/your/app", "code --goto \"{file}:{line}\"",
        "MIT License · Copyright © Rasyidf 2023-2026", "  ·  SHA-256 ", "Audit", "Editor", "OK", "Framework", "Folder", "AI", "Model", "Endpoint",
    ];

    private static string AvaloniaProjectDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Toucan.Avalonia"));

    private static IEnumerable<(string Source, string Text)> UiLiterals()
    {
        var dir = AvaloniaProjectDir();
        foreach (var file in Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories))
        {
            var xaml = File.ReadAllText(file);
            foreach (Match m in XamlLiteral().Matches(xaml))
                yield return (Path.GetRelativePath(dir, file), WebUtility.HtmlDecode(m.Groups[1].Value).Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
        }

        var menu = File.ReadAllText(Path.Combine(dir, "Views", "MainMenu.cs"));
        foreach (Match m in MenuLiteral().Matches(menu))
            if (!m.Groups[1].Value.Contains('{', StringComparison.Ordinal)) yield return ("Views/MainMenu.cs", m.Groups[1].Value);

        var window = File.ReadAllText(Path.Combine(dir, "Views", "MainWindow.axaml.cs"));
        foreach (Match m in ActionTip().Matches(window)) yield return ("Views/MainWindow.axaml.cs", m.Groups[1].Value);

        var app = File.ReadAllText(Path.Combine(dir, "App.axaml.cs"));
        foreach (Match m in PanelTitle().Matches(app)) yield return ("App.axaml.cs", m.Groups[1].Value);
    }

    [GeneratedRegex(@"\{loc:Loc '((?:[^'\\]|\\.)*)'\}")] private static partial Regex XamlLiteral();
    [GeneratedRegex(@"new(?: Item)?\(""((?:[^""\\]|\\.)*)""")] private static partial Regex MenuLiteral();
    [GeneratedRegex(@"\(FASymbol\.\w+, ""([^""]+)""")] private static partial Regex ActionTip();
    [GeneratedRegex(@"BuiltInSidePanel\(""[^""]+"", ""([^""]+)""")] private static partial Regex PanelTitle();

    [Fact]
    public void EveryUiStringHasAnIndonesianTranslation()
    {
        var table = Loc.LoadAll()["id-ID"];
        var missing = UiLiterals().Concat(OptionsViewModel.Pages.Select(p => (Source: "OptionsViewModel.Pages", Text: p)))
            .Where(l => l.Text.Any(char.IsLetter) && !Untranslated.Contains(l.Text) && !table.ContainsKey(l.Text))
            .Select(l => $"{l.Source}: {l.Text}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, "Add these to Locales/Strings.id-ID.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void NoTranslationIsEmptyOrEqualToItsKey()
    {
        var bad = Loc.LoadAll()["id-ID"]
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value) || (kv.Key == kv.Value && !Untranslated.Contains(kv.Key)))
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(bad.Count == 0, "Empty or untranslated entries:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void TranslationsKeepTheirPlaceholdersAndMnemonics()
    {
        foreach (var (english, translated) in Loc.LoadAll()["id-ID"])
        {
            Assert.True(Placeholders(english).SequenceEqual(Placeholders(translated)), $"placeholders differ: '{english}' → '{translated}'");
            Assert.True(english.Contains('_') == translated.Contains('_'), $"mnemonic differs: '{english}' → '{translated}'");
        }

        static IEnumerable<string> Placeholders(string s) => Regex.Matches(s, @"%\d|\{\{\w+\}\}|\{\d\}|%s|:param").Select(m => m.Value).Order();
    }

    [AvaloniaFact]
    public void IndonesianRendersTheMenuTheSettingsPagesAndPanelTitles()
    {
        try
        {
            Loc.Use("id-ID");
            using var host = new TestHost();
            var vm = host.CreateViewModel();
            var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
            window.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // macOS shows a native menu (mnemonic underscores stripped); Windows and Linux show an in-window Menu.
            var headers = OperatingSystem.IsMacOS()
                ? NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(m => m.Header).ToList()
                : window.GetVisualDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
            var mnemonic = OperatingSystem.IsMacOS() ? "" : "_";
            Assert.Contains(mnemonic + "Berkas", headers);
            Assert.Contains(mnemonic + "Bantuan", headers);

            var options = host.Services.GetRequiredService<OptionsViewModel>();
            var dialog = new OptionsDialog(options);
            dialog.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var pages = dialog.GetVisualDescendants().OfType<ListBox>().First().GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Umum", pages);
            Assert.Contains("Integrasi", pages);
            for (var i = 0; i < OptionsViewModel.Pages.Count; i++)
            {
                options.SelectedPageIndex = i;
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.NotNull(dialog.CaptureRenderedFrame());
            }
            dialog.Close();

            Assert.Equal("Inspektur", Loc.T("Inspector"));
            window.Close();
        }
        finally
        {
            Loc.Use("en-US");
        }
    }

    [Fact]
    public void UnknownTextAndEnglishFallBackToTheKey()
    {
        try
        {
            Loc.Use("id-ID");
            Assert.Equal("Something new", Loc.T("Something new"));
            Loc.Use("en-US");
            Assert.Equal("Inspector", Loc.T("Inspector"));
            Loc.Use("xx-YY");
            Assert.Equal("Inspector", Loc.T("Inspector"));
        }
        finally
        {
            Loc.Use("en-US");
        }
    }
}
