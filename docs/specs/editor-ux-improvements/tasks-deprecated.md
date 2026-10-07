---
title: "editor-ux-improvements — tasks"
status: deprecated
progress: "0/21 tasks"
updated: 2026-07-06
summary: "Task list for the WPF editor UX changes. Never started; the WPF app was removed."
archived: 2026-10-07
reason: "Never started; targeted WPF views and the WPF app was removed"
---
# Implementation Plan: Editor UX Improvements

## Overview

Five localized UI-layer changes to the WPF translation editor. No new services or dependencies — all work touches existing ViewModels, XAML views, and code-behind. Tasks are ordered so foundational ViewModel changes land first, then XAML consumes them.

## Tasks

- [ ] 1. Mode Selector Bar — active pill elevation and MinWidth
  - [ ] 1.1 Add DropShadowEffect + BorderBrush accent on IsChecked=True trigger in ModeSelectorBar.xaml
    - In the `SegmentedPill` style, add `Effect` (DropShadowEffect ShadowDepth=1, BlurRadius=3, Opacity=0.15) and `BorderBrush=ControlElevationBorderBrush` with `BorderThickness=1` to the IsChecked=True trigger setters targeting `Bd`
    - Add `MinWidth="56"` to the base RadioButton setters
    - _Requirements: 1.3, 1.4_
  - **Verification:** `dotnet build Toucan\Toucan.csproj` — no errors/warnings. Visual: active pill has subtle shadow and border.

- [ ] 2. Multi-line TextBox height cap
  - [ ] 2.1 Add MaxHeight and vertical scroll to value TextBox in TranslationItemView.xaml
    - On the existing `ui:TextBox` that already has `AcceptsReturn="True"` and `TextWrapping="Wrap"`, add `MaxHeight="200"` and `VerticalScrollBarVisibility="Auto"`
    - _Requirements: 2.3_
  - **Verification:** `dotnet build Toucan\Toucan.csproj` — no errors/warnings.

- [ ] 3. Review/Audit action buttons — ViewModel commands
  - [ ] 3.1 Add ViewCommentCommand and ClearApprovedCommand to TranslationItemViewModel.cs
    - Add `[RelayCommand] private void ViewComment()` — copies `Comment` to clipboard if non-empty
    - Add `[RelayCommand] private void ClearApproved() => IsApproved = false;`
    - _Requirements: 3.1, 3.2, 3.4_
  - [ ] 3.2 Add comment button and needs-review button XAML to TranslationItemView.xaml
    - Expand the language-row Grid columns to include Col 4 (comment button, visible in Review+Audit) and Col 5 (needs-review button, visible in Review only)
    - Comment button: `Command="{Binding ViewCommentCommand}"`, SymbolIcon Comment16
    - Needs-review button: `Command="{Binding ClearApprovedCommand}"`, SymbolIcon DismissCircle16
    - Use DataTrigger on `PanelService.Instance.EditorMode` to control Visibility
    - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.6_
  - **Verification:** `dotnet build Toucan\Toucan.csproj` — no errors/warnings.

- [ ] 4. Checkpoint — build and test
  - Ensure `dotnet build Toucan\Toucan.csproj` succeeds with zero warnings.
  - Run `dotnet test tests\Toucan.Tests` — all existing tests pass.
  - Ask the user if questions arise.

- [ ] 5. Language visibility filtering — ViewModel layer
  - [ ] 5.1 Create LanguageVisibilityItem class
    - New file `Toucan/ViewModels/LanguageVisibilityItem.cs`
    - `public partial class LanguageVisibilityItem : ObservableObject` with `[ObservableProperty] private string language` and `[ObservableProperty] private bool isVisible = true`
    - _Requirements: 4.2_
  - [ ] 5.2 Add IsLanguageVisible property to TranslationItemViewModel.cs
    - Add `[ObservableProperty] private bool isLanguageVisible = true;`
    - _Requirements: 4.3, 4.4_
  - [ ] 5.3 Add LanguageVisibilityFilter collection and apply-visibility logic to MainWindowViewModel
    - Add `[ObservableProperty] private ObservableCollection<LanguageVisibilityItem> languageVisibilityFilter = [];` in the Nav partial
    - Add helper method `ApplyLanguageVisibility()` that iterates displayed groups and sets each `TranslationItemViewModel.IsLanguageVisible` based on the filter state
    - Subscribe to `LanguageVisibilityItem.PropertyChanged` to call `ApplyLanguageVisibility()` on toggle
    - Initialize the collection when a project loads (in existing ProjectChanged handler) with all languages visible
    - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6_
  - [ ] 5.4 Add language visibility checkbox row in TranslationDetailsView.xaml
    - Add horizontal `ItemsControl` with `WrapPanel` above the existing filter TextBox
    - Bind `ItemsSource="{Binding LanguageVisibilityFilter}"`, each item is a `CheckBox` with `Content="{Binding Language}"` and `IsChecked="{Binding IsVisible, Mode=TwoWay}"`
    - _Requirements: 4.1, 4.2_
  - [ ] 5.5 Bind Language_Row visibility in TranslationItemView.xaml
    - Add `Visibility="{Binding IsLanguageVisible, Converter={StaticResource BoolToVis}}"` on the language-row root Grid
    - _Requirements: 4.3, 4.4_
  - [ ]*  5.6 Write property tests for language visibility filter
    - **Property 1: Language visibility filter count matches project languages**
    - **Property 2: Language visibility filtering correctness**
    - **Validates: Requirements 4.2, 4.3, 4.4, 4.5**
  - **Verification:** `dotnet build Toucan\Toucan.csproj` && `dotnet test tests\Toucan.Tests`

- [ ] 6. Inspector panel suggestion insertion
  - [ ] 6.1 Add FocusedTranslationItem and InsertSuggestionCommand to MainWindowViewModel
    - Add `[ObservableProperty] private TranslationItemViewModel? focusedTranslationItem;` in the Nav partial
    - Add `[RelayCommand] private void InsertSuggestion(string? suggestion)` — sets `FocusedTranslationItem.Value = suggestion` if focused, else shows status bar message
    - _Requirements: 5.2, 5.3, 5.4_
  - [ ] 6.2 Track focused translation item from TranslationItemView.xaml.cs
    - Handle `GotFocus` on the value TextBox in code-behind
    - Walk DataContext chain to find `MainWindowViewModel`, set `FocusedTranslationItem` to the current item's ViewModel
    - _Requirements: 5.2_
  - [ ] 6.3 Add insert button in InspectorView.xaml suggestion item template
    - Add `ui:Button` (SymbolIcon ArrowImport16, Appearance=Transparent) in a new column beside each suggestion
    - Bind `Command` to `MainWindowViewModel.InsertSuggestionCommand` via RelativeSource AncestorType
    - Bind `CommandParameter` to the suggestion text
    - Hide in Audit mode via DataTrigger on `PanelService.Instance.EditorMode`
    - _Requirements: 5.1, 5.5_
  - [ ]* 6.4 Write property test for suggestion insertion
    - **Property 3: Suggestion insertion replaces value and marks dirty**
    - **Validates: Requirements 5.2, 5.4**
  - **Verification:** `dotnet build Toucan\Toucan.csproj` && `dotnet test tests\Toucan.Tests`

- [ ] 7. Final checkpoint
  - `dotnet build Toucan\Toucan.csproj` — zero warnings
  - `dotnet test tests\Toucan.Core.Tests` — all pass
  - `dotnet test tests\Toucan.Tests` — all pass
  - Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional property-based tests (can be skipped for faster MVP).
- Requirements 1.1, 1.2, 2.1, 2.2, 2.4, 2.5, 3.3, 3.5, 3.6 are already satisfied by the existing codebase — no implementation needed.
- All changes are session-scoped (no persistent settings added).
- No new NuGet dependencies required.
