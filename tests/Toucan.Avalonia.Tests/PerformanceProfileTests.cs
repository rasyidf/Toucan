using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>
/// V1-50: the reference fixture, 10,000 keys in 10 languages (an i18next project, one 10,000-key file per language), opened,
/// searched, edited and saved through the real editor in a headless window. Opt-in, because it measures rather than checks:
/// run it in Release with TOUCAN_PERF=1, optionally TOUCAN_PERF_REPORT=&lt;file&gt; for the JSON. The budgets it reports against
/// are in docs/performance.md.
/// </summary>
public sealed class PerformanceProfileTests
{
    /// <summary>10,000 for the reference fixture; TOUCAN_PERF_KEYS sets another size to see how time grows.</summary>
    private static readonly int Keys = int.TryParse(Environment.GetEnvironmentVariable("TOUCAN_PERF_KEYS"), out var keys) ? keys : 10_000;
    private static readonly string[] s_languages = ["en", "fr", "de", "es", "it", "pt", "nl", "pl", "ja", "ko"];
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    private static void CreateFixture(string root)
    {
        foreach (var language in s_languages)
        {
            var sections = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            for (var k = 0; k < Keys; k++)
            {
                var section = $"section{k / 100:000}";
                if (!sections.TryGetValue(section, out var keys)) sections[section] = keys = new(StringComparer.Ordinal);
                keys[$"key{k:00000}"] = $"Message {k} in {language} for {{{{name}}}}, shown on screen {k % 400}";
            }
            var folder = Path.Combine(root, "locales", language);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "common.json"), JsonSerializer.Serialize(sections, s_indented) + "\n");
        }
        new ProjectSettings { ProjectPath = root, Framework = "i18next", SaveFormat = FormatIds.Namespaced, PrimaryLanguage = "en", Languages = [.. s_languages] }.Save();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>Runs <paramref name="work"/> while measuring the longest wait for an input-priority job on the UI thread.</summary>
    private static async Task<(double Total, double LongestStall)> WithUiLatency(Func<Task> work)
    {
        var watch = Stopwatch.StartNew();
        var task = work();
        double longest = 0;
        while (!task.IsCompleted)
        {
            var asked = watch.Elapsed.TotalMilliseconds;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            longest = Math.Max(longest, watch.Elapsed.TotalMilliseconds - asked);
            await Task.Delay(5);
        }
        await task;
        return (watch.Elapsed.TotalMilliseconds, longest);
    }

    [AvaloniaFact]
    public async Task ProfileTheReferenceFixture()
    {
        if (Environment.GetEnvironmentVariable("TOUCAN_PERF") != "1")
            Assert.Skip("Performance profile; run with TOUCAN_PERF=1 in Release (docs/performance.md).");

        using var host = new TestHost();
        var folder = Directory.CreateDirectory(Path.Combine(host.Root, "reference")).FullName;
        CreateFixture(folder);
        var results = new Dictionary<string, double>();
        // Each phase is logged as it ends, so a phase that hangs shows in the log of an unfinished run.
        var log = Environment.GetEnvironmentVariable("TOUCAN_PERF_LOG");
        var clock = Stopwatch.StartNew();
        void Phase(string name)
        {
            if (log is { Length: > 0 }) File.AppendAllText(log, $"{clock.Elapsed.TotalSeconds,8:F1}s  {name}{Environment.NewLine}");
        }
        Phase("start");
        var process = Process.GetCurrentProcess();
        GC.Collect();
        var baseMemory = GC.GetTotalMemory(forceFullCollection: true);

        Phase("fixture written");
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        try
        {
            // Open until the first page of the editor is laid out: when the user can start working.
            var watch = Stopwatch.StartNew();
            await vm.OpenProjectAsync(folder);
            Settle();
            results["openFirstMs"] = watch.Elapsed.TotalMilliseconds;
            Phase("opened");
            if (Environment.GetEnvironmentVariable("TOUCAN_PERF_OPEN_ONLY") == "1") { File.AppendAllText(log ?? Path.GetTempFileName(), $"keys={Keys} openFirstMs={watch.Elapsed.TotalMilliseconds:F0}{Environment.NewLine}"); return; }
            Assert.Equal(Keys * s_languages.Length, vm.AllTranslation.Count);
            Assert.NotEmpty(window.GetVisualDescendants().OfType<TranslationCard>());

            GC.Collect();
            results["managedMemoryMb"] = (GC.GetTotalMemory(forceFullCollection: true) - baseMemory) / 1024.0 / 1024.0;
            process.Refresh();
            results["workingSetMb"] = process.WorkingSet64 / 1024.0 / 1024.0;

            watch.Restart();
            await vm.OpenProjectAsync(folder);
            Settle();
            results["openWarmMs"] = watch.Elapsed.TotalMilliseconds;
            Phase("reopened");

            // Warm search, after one search has run: a key path and a word in values.
            vm.Search("key05432");
            var keySearch = new List<double>();
            var valueSearch = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                watch.Restart(); vm.Search($"key0{5432 + i}"); Settle(); keySearch.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart(); vm.Search($"screen {317 + i}"); Settle(); valueSearch.Add(watch.Elapsed.TotalMilliseconds);
            }
            results["searchKeyWarmMs"] = Median(keySearch);
            results["searchValueWarmMs"] = Median(valueSearch);
            Phase("searched");
            vm.Search(string.Empty);
            Settle();

            // One edit, as typing commits it.
            var row = vm.PagingController.Data[0];
            var item = row.Translations.First(t => t.Language == "fr");
            watch.Restart();
            item.Value = "Édité une fois";
            Settle();
            results["editMs"] = watch.Elapsed.TotalMilliseconds;
            Phase("edited");

            var (saveOne, stallOne) = await WithUiLatency(() => vm.SaveCommand.ExecuteAsync(null));
            results["saveOneFileMs"] = saveOne;
            results["saveOneFileLongestUiStallMs"] = stallOne;
            Phase("saved one file");

            // An edit in every language, so the save rewrites all ten 10,000-key files.
            foreach (var translation in row.Translations) translation.Value = translation.Value + " (2)";
            Settle();
            var (saveAll, stallAll) = await WithUiLatency(() => vm.SaveCommand.ExecuteAsync(null));
            results["saveAllFilesMs"] = saveAll;
            results["saveAllFilesLongestUiStallMs"] = stallAll;
            Phase("saved all files");
            Assert.False(vm.IsDirty);
        }
        finally
        {
            window.Close();
        }

        var report = new
        {
            fixture = $"{Keys} keys x {s_languages.Length} languages (i18next, one file per language)",
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            results = results.ToDictionary(p => p.Key, p => Math.Round(p.Value, 1)),
        };
        var json = JsonSerializer.Serialize(report, s_indented);
        TestContext.Current.TestOutputHelper!.WriteLine(json);
        if (Environment.GetEnvironmentVariable("TOUCAN_PERF_REPORT") is { Length: > 0 } path) File.WriteAllText(path, json + "\n");
    }
}
