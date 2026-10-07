---
title: "editor-ux-improvements — requirements"
status: deprecated
progress: "0/21 tasks"
updated: 2026-07-06
summary: "Requirements for five WPF editor UX changes (mode selector pill and more). Never started; the WPF app was removed."
archived: 2026-10-07
reason: "Never started; targeted WPF views and the WPF app was removed"
---
# Requirements Document

## Introduction

Improve the translation editor UX across five areas: editor mode visual differentiation, multi-line rich text editing, review/audit workflow actions, language visibility toggling, and inspector panel suggestion insertion. All changes target the WPF desktop client (Toucan/) using existing WPF-UI 4.3 controls, CommunityToolkit.Mvvm patterns, and the established panel/service architecture.

## Glossary

- **Mode_Selector_Bar**: The segmented control (`ModeSelectorBar.xaml`) rendered in the title bar that switches between Editor, Review, and Audit modes.
- **Editor_Mode**: The currently active operating mode of the translation editor (Editor, Review, or Audit), represented by the `EditorMode` enum.
- **Translation_Item_View**: The per-translation editing control (`TranslationItemView.xaml`) that displays a key's language rows with value editors.
- **Language_Row**: A single row within a Translation_Item_View showing a language code label and a text input for the translation value.
- **Inspector_Panel**: The right-slot panel (`InspectorView.xaml`) with Suggestions, Details, and Validation tabs.
- **Suggestion_Entry**: A single suggestion item displayed in the Inspector_Panel Suggestions tab, originating from Translation Memory, Machine Translation, or Dictionary panels.
- **Language_Visibility_Filter**: A set of checkboxes above the editor area allowing users to toggle which languages appear in Translation_Item_View cards.
- **Panel_Service**: The singleton service (`PanelService`) managing layout, panel visibility, editor mode state, and zen mode.
- **Translation_Item_ViewModel**: The per-language-row ViewModel (`TranslationItemViewModel.cs`) exposing Value, Language, IsApproved, and editing logic.
- **Language_Group_ViewModel**: The per-key card ViewModel (`LanguageGroupViewModel.cs`) exposing Translations collection and namespace display.

## Requirements

### Requirement 1: Editor Mode Visual Differentiation

**User Story:** As a translator, I want the mode selector bar to clearly show which mode is active, so that I always know whether I am editing, reviewing, or auditing translations.

#### Acceptance Criteria

1. WHEN the Editor_Mode changes, THE Mode_Selector_Bar SHALL display the active pill with a visually distinct background that contrasts with inactive pills (using `CardBackgroundFillColorDefaultBrush` for active, transparent for inactive).
2. WHEN the Editor_Mode changes, THE Mode_Selector_Bar SHALL render the active pill text in `TextFillColorPrimaryBrush` with SemiBold weight, and inactive pill text in `TextFillColorSecondaryBrush` with Normal weight.
3. WHEN the Editor_Mode changes, THE Mode_Selector_Bar SHALL apply an elevation shadow or border accent to the active pill to provide depth separation from the container background.
4. THE Mode_Selector_Bar SHALL render all three mode options (Editor, Review, Audit) within the segmented control container, each with a reasonable minimum width and sizing that accommodates label text length.

### Requirement 2: Multi-Line Translation Value Editing

**User Story:** As a translator, I want the translation value input to support multi-line text with proper wrapping, so that I can comfortably edit long translation strings.

#### Acceptance Criteria

1. THE Translation_Item_View SHALL render value inputs using a multi-line TextBox with `TextWrapping="Wrap"` and `AcceptsReturn="True"`.
2. WHEN a translation value exceeds the visible width of the input, THE Translation_Item_View SHALL wrap text to additional lines rather than scrolling horizontally.
3. WHEN a translation value content height reaches or exceeds 200 pixels, THE Translation_Item_View value input SHALL activate vertical scrolling rather than continuing to expand.
4. WHILE the Editor_Mode is Audit, THE Translation_Item_View value input SHALL remain read-only regardless of content length.
5. THE Translation_Item_View value input SHALL have a minimum height of 28 pixels to maintain consistent row spacing when content is short.

### Requirement 3: Review and Audit Mode Action Buttons

**User Story:** As a reviewer, I want comment and approval action buttons visible during Review and Audit modes, so that I can efficiently annotate and approve translations.

#### Acceptance Criteria

1. WHILE the Editor_Mode is Review, THE Translation_Item_View SHALL display a comment button on each Language_Row allowing the user to add or view comments.
2. WHILE the Editor_Mode is Audit, THE Translation_Item_View SHALL display a comment button on each Language_Row allowing the user to view existing comments.
3. WHILE the Editor_Mode is Review, THE Translation_Item_View SHALL display an approve toggle button on each Language_Row that sets the IsApproved state of the Translation_Item_ViewModel.
4. WHILE the Editor_Mode is Review, THE Translation_Item_View SHALL display a needs-review button on each Language_Row that clears the IsApproved state of the Translation_Item_ViewModel.
5. THE Translation_Item_View SHALL ensure the approve toggle button visual state always matches the IsApproved state — WHEN IsApproved is True, THE button SHALL render with a checkmark icon and accent-colored background.
6. WHEN the IsApproved state of a Translation_Item_ViewModel is False, THE approve toggle button SHALL render with a neutral icon and no accent background.

### Requirement 4: Language Visibility Checkboxes

**User Story:** As a translator working on a subset of target languages, I want to toggle which languages are shown in editor cards, so that I can focus on only the languages I am responsible for.

#### Acceptance Criteria

1. THE Language_Visibility_Filter SHALL appear as a horizontal row of checkboxes positioned beside the search bar at the top of the editor area.
2. THE Language_Visibility_Filter SHALL display one checkbox per language in the current project, labeled with the language code.
3. WHEN a Language_Visibility_Filter checkbox is checked, THE Translation_Item_View SHALL display the Language_Row for that language across all visible cards, regardless of whether other checkboxes are also checked.
4. WHEN a Language_Visibility_Filter checkbox is unchecked, THE Translation_Item_View SHALL hide the Language_Row for that language across all visible cards.
5. WHEN the project is opened, THE Language_Visibility_Filter SHALL initialize with all checkboxes checked (all languages visible).
6. THE Language_Visibility_Filter SHALL persist the selected languages within the current session so toggling pages does not reset visibility.

### Requirement 5: Inspector Panel Suggestion Insertion

**User Story:** As a translator, I want to insert suggestions from the Inspector panel directly into the active translation field, so that I can apply Translation Memory, Machine Translation, or Dictionary results without manual copy-paste.

#### Acceptance Criteria

1. WHEN a Suggestion_Entry is displayed in the Inspector_Panel Suggestions tab, THE Inspector_Panel SHALL display an insert button beside each Suggestion_Entry.
2. WHEN the user clicks the insert button on a Suggestion_Entry, THE Inspector_Panel SHALL replace the value of the currently focused Translation_Item_ViewModel with the suggestion text.
3. IF no Translation_Item_ViewModel is currently focused WHEN the user clicks an insert button, THEN THE Inspector_Panel SHALL display a status message indicating no target field is selected.
4. WHEN a suggestion is inserted into a Translation_Item_ViewModel, THE Translation_Item_ViewModel SHALL mark the item as dirty and trigger the standard save-debounce logic.
5. WHILE the Editor_Mode is Audit, THE Inspector_Panel SHALL hide the insert buttons on Suggestion_Entries since editing is not permitted.
