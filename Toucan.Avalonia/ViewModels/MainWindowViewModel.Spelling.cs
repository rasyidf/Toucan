using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnProjectSettingsChanged(ProjectSettings? value)
    {
        _spellingValidation?.Cancel();
        _spellingSession?.Dispose(); _spellingSession = null;
        if (value == null || _spellingService == null || _dictionaries == null) return;
        _spellingSession = new SpellingSession(_spellingService, _dictionaries, () => value.Spelling, (locale, word) =>
        {
            if (!ReferenceEquals(value, ProjectSettings)) return;
            var before = SpellingSettings.Copy(value.Spelling);
            var normalized = DictionaryRegistry.NormalizeLocale(locale);
            var words = value.Spelling.Words.GetValueOrDefault(normalized)?.ToList() ?? [];
            if (words.Contains(word, StringComparer.Ordinal) || words.Count >= 10000) return;
            words.Add(word); value.Spelling.Words[normalized] = words;
            _undoRedoService.RecordSpelling(before, value.Spelling);
            _translationStore.NotifyConfigurationChanged(true); MarkStructureChanged();
        });
    }
}
