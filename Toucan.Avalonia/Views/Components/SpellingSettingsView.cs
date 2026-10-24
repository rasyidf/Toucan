using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Views.Components;

public sealed class SpellingSettingsView : UserControl
{
    private readonly ProjectSettings _settings;
    private readonly DictionaryRegistry _registry;
    private readonly IDialogService _dialogs;
    private readonly ListBox _packs = new() { MaxHeight = 130 };
    private readonly TextBlock _status = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _localeDetails = new() { Spacing = 6 };
    private CancellationTokenSource? _import;
    private string _locale;
    public SpellingSettingsView(ProjectSettings settings, DictionaryRegistry registry, IDialogService dialogs)
    {
        _settings = settings; _registry = registry; _dialogs = dialogs; _locale = settings.PrimaryLanguage;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = Loc.T("Local spelling"), FontSize = 20 });
        var enabled = new CheckBox { Content = Loc.T("Check spelling in this project"), IsChecked = settings.Spelling.Enabled }; enabled.IsCheckedChanged += (_, _) => settings.Spelling.Enabled = enabled.IsChecked == true; body.Children.Add(enabled);
        body.Children.Add(new TextBlock { Text = Loc.T("Dictionary library imports, removals and user word lists apply immediately. Project settings apply when this dialog is saved. Imported packs are not approved for redistribution."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = Loc.T("Locale for a plain dictionary pair (optional)") });
        var importLocale = new TextBox { PlaceholderText = "en-US" }; body.Children.Add(importLocale);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var import = new Button { Content = Loc.T("Import dictionary folder") }; import.Click += async (_, _) =>
        {
            var folder = await dialogs.SelectFolderAsync(null, Loc.T("Dictionary folder (.aff/.dic or .xcu)")); if (folder == null) return;
            var requestedLocale = string.IsNullOrWhiteSpace(importLocale.Text) ? null : importLocale.Text;
            using var cancellation = new CancellationTokenSource(); _import = cancellation; import.IsEnabled = false; _status.Text = Loc.T("Loading local dictionaries…");
            try { var packs = await Task.Run(() => registry.ImportFolderAsync(folder, requestedLocale, cancellation.Token)); _status.Text = $"{packs.Count} {Loc.T("dictionary pack(s) imported")}"; RefreshPacks(); RenderLocale(); }
            catch (OperationCanceledException) { _status.Text = Loc.T("Dictionary import cancelled."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException or InvalidOperationException) { _status.Text = ex.Message; }
            finally { _import = null; import.IsEnabled = true; }
        }; actions.Children.Add(import);
        var cancel = new Button { Content = Loc.T("Cancel import") }; cancel.Click += (_, _) => _import?.Cancel(); actions.Children.Add(cancel);
        var remove = new Button { Content = Loc.T("Remove from library") }; remove.Click += (_, _) => { if (_packs.SelectedItem is DictionaryPack pack) { try { registry.Remove(pack.Id); RefreshPacks(); RenderLocale(); _status.Text = Loc.T("Pack removed from the registry. Local copied files are retained."); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; } } }; actions.Children.Add(remove);
        body.Children.Add(actions); body.Children.Add(_packs);
        _packs.DisplayMemberBinding = new global::Avalonia.Data.Binding("Name");
        _packs.SelectionChanged += (_, _) => { if (_packs.SelectedItem is DictionaryPack pack) _status.Text = $"{pack.Name}\n{string.Join(", ", pack.Locales)}\n{pack.Origin}\n{pack.License}\nAFF SHA256 {pack.AffixSha256}\nDIC SHA256 {pack.DictionarySha256}\n{Loc.T("Notices")}: {string.Join(", ", pack.Notices)}"; };
        body.Children.Add(_status);
        var locales = (settings.ResourceDocuments.Count > 0 ? settings.ResourceDocuments.Select(d => settings.LanguageAliases?.GetValueOrDefault(d.Resource.Language) ?? d.Resource.Language) : settings.Languages).Distinct().ToList();
        _locale = locales.Contains(settings.PrimaryLanguage) ? settings.PrimaryLanguage : locales.FirstOrDefault() ?? settings.PrimaryLanguage;
        var locale = new ComboBox { ItemsSource = locales, SelectedItem = _locale, HorizontalAlignment = HorizontalAlignment.Stretch };
        locale.SelectionChanged += (_, _) => { if (locale.SelectedItem is string selected) { _locale = selected; RenderLocale(); } }; body.Children.Add(locale); body.Children.Add(_localeDetails);
        global::Avalonia.Automation.AutomationProperties.SetName(_packs, Loc.T("Local spelling"));
        global::Avalonia.Automation.AutomationProperties.SetName(locale, Loc.T("Locale"));
        global::Avalonia.Automation.AutomationProperties.SetName(importLocale, Loc.T("Locale for a plain dictionary pair (optional)"));
        Content = body; RefreshPacks(); RenderLocale();
        DetachedFromVisualTree += (_, _) => _import?.Cancel();
    }
    private void RefreshPacks() { _packs.ItemsSource = _registry.Packs; if (_registry.LoadStatus.Length > 0) _status.Text = _registry.LoadStatus; }
    private sealed record PackChoice(string Id, string Label);
    private void RenderLocale()
    {
        _localeDetails.Children.Clear();
        string locale;
        try { locale = DictionaryRegistry.NormalizeLocale(_locale); }
        catch (FormatException ex) { _status.Text = ex.Message; return; }
        var resolution = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        _localeDetails.Children.Add(new TextBlock { Text = Loc.T("Explicit fallback locale (empty means exact locale only)") });
        var fallback = new TextBox { Text = _settings.Spelling.Fallbacks.GetValueOrDefault(locale) ?? "" };
        fallback.TextChanged += (_, _) => { if (string.IsNullOrWhiteSpace(fallback.Text)) _settings.Spelling.Fallbacks.Remove(locale); else _settings.Spelling.Fallbacks[locale] = fallback.Text; UpdateResolution(); };
        global::Avalonia.Automation.AutomationProperties.SetName(fallback, Loc.T("Explicit fallback locale (empty means exact locale only)"));
        _localeDetails.Children.Add(fallback);
        var choices = new[] { new PackChoice("", Loc.T("Automatic exact match")) }.Concat(_registry.Packs.Select(p => new PackChoice(p.Id, p.Name + " · " + string.Join(", ", p.Locales) + " · " + p.Id[..8]))).ToList();
        var assigned = new ComboBox { ItemsSource = choices, DisplayMemberBinding = new global::Avalonia.Data.Binding("Label"), SelectedItem = choices.FirstOrDefault(p => p.Id == _settings.Spelling.Assignments.GetValueOrDefault(locale)) ?? choices[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        assigned.SelectionChanged += (_, _) => { if (assigned.SelectedItem is PackChoice choice) { if (choice.Id.Length == 0) _settings.Spelling.Assignments.Remove(locale); else _settings.Spelling.Assignments[locale] = choice.Id; UpdateResolution(); } };
        global::Avalonia.Automation.AutomationProperties.SetName(assigned, Loc.T("Local spelling"));
        _localeDetails.Children.Add(assigned);
        _localeDetails.Children.Add(resolution);
        _localeDetails.Children.Add(new TextBlock { Text = Loc.T("Project word list (one word per line)") });
        var projectWords = new TextBox { Text = string.Join('\n', _settings.Spelling.Words.GetValueOrDefault(locale) ?? []), AcceptsReturn = true, MinHeight = 80, MaxHeight = 160 };
        global::Avalonia.Automation.AutomationProperties.SetName(projectWords, Loc.T("Project word list (one word per line)"));
        projectWords.TextChanged += (_, _) => _settings.Spelling.Words[locale] = Lines(projectWords.Text); _localeDetails.Children.Add(projectWords);
        _localeDetails.Children.Add(new TextBlock { Text = Loc.T("User word list (one word per line)") });
        var userWords = new TextBox { Text = string.Join('\n', _registry.UserWords(locale)), AcceptsReturn = true, MinHeight = 80, MaxHeight = 160 }; _localeDetails.Children.Add(userWords);
        global::Avalonia.Automation.AutomationProperties.SetName(userWords, Loc.T("User word list (one word per line)"));
        var saveWords = new Button { Content = Loc.T("Save user word list") }; saveWords.Click += (_, _) => { try { _registry.SetUserWords(locale, Lines(userWords.Text)); _status.Text = Loc.T("User word list saved."); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { _status.Text = ex.Message; } }; _localeDetails.Children.Add(saveWords);
        UpdateResolution();
        void UpdateResolution() { try { resolution.Text = _registry.Resolve(locale, _settings.Spelling).Status; } catch (FormatException ex) { resolution.Text = ex.Message; } }
    }
    private static List<string> Lines(string? text) => (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
}
