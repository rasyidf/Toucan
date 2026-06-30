# Known Bugs

## Active

### Statusbar language selector shows empty menu
- **Location**: StatusBarView.xaml → Default Language button context menu
- **Symptom**: Clicking the language code in the statusbar opens an empty popup
- **Cause**: `StatusBarViewModel.AvailableLanguages` collection is never populated. Need to fill it from the loaded project's discovered languages (in `MainWindowViewModel` after project load, sync to `StatusBarViewModel.AvailableLanguages`).
- **Fix**: After project loads in `UpdateUiAfterProjectLoad`, populate `statusBarViewModel.AvailableLanguages` from `AllTranslation.Select(t => t.Language).Distinct()`.

## Fixed

_(none yet)_
