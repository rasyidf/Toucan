# Design: Editor UX Improvements

## Overview

Five focused improvements to the translation editor UX, all implementable within the existing MVVM + WPF-UI architecture. No new infrastructure or services are introduced — changes are localized to existing ViewModels, XAML views, and PanelService.

**Approach:** Minimal diff per requirement. Reuse existing patterns (DataTriggers on `PanelService.Instance.EditorMode`, `ObservableProperty`, `RelayCommand`, `BooleanToVisibilityConverter`). No new NuGet dependencies.

## Architecture

No architectural changes. All five requirements are UI-layer concerns resolved by:

1. XAML style/trigger changes (Req 1, 2, 3)
2. A new `ObservableCollection<LanguageVisibility>` on `MainWindowViewModel` + a `CollectionViewSource` filter (Req 4)
3. A new `InsertSuggestionCommand` on `MainWindowViewModel` + tracking focused `TranslationItemViewModel` (Req 5)

```
MainWindowViewModel (existing)
├── VisibleLanguages: ObservableCollection<LanguageVisibilityItem>  [NEW]
├── FocusedTranslationItem: TranslationItemViewModel?              [NEW]
├── InsertSuggestionCommand                                        [NEW]
└── (existing EditorMode, Suggestions, SelectedGroup, etc.)

TranslationDetailsView.xaml (existing)
└── adds LanguageVisibilityFilter row above filter TextBox         [NEW]

TranslationItemView.xaml (existing)
└── adds comment button, MaxHeight=200, shadow on mode pills       [MODIFIED]

ModeSelectorBar.xaml (existing)
└── adds Effect/BorderBrush on active pill trigger                 [MODIFIED]

InspectorView.xaml (existing)
└── adds insert button per suggestion item                         [MODIFIED]
```

## Components and Interfaces

### Requirement 1: Mode Selector Bar Differentiation

**Current state:** `ModeSelectorBar.xaml` already has a `SegmentedPill` RadioButton style with `IsChecked=True` triggers that set `Background=CardBackgroundFillColorDefaultBrush`, `FontWeight=SemiBold`, and `Foreground=TextFillColorPrimaryBrush`. Requirements 1.1 and 1.2 are already satisfied.

**Change:** Add elevation/depth to the active pill (Req 1.3) via a `DropShadowEffect` or `BorderBrush` accent on the `IsChecked=True` trigger. Add `MinWidth="56"` on each pill (Req 1.4).

```xml
<!-- In SegmentedPill style, IsChecked=True trigger, add: -->
<Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource ControlElevationBorderBrush}" />
<Setter TargetName="Bd" Property="BorderThickness" Value="1" />
<Setter TargetName="Bd" Property="Effect">
    <Setter.Value>
        <DropShadowEffect ShadowDepth="1" BlurRadius="3" Opacity="0.15" Color="Black"/>
    </Setter.Value>
</Setter>
```

Also add `MinWidth="56"` to the RadioButton base setters to ensure pills accommodate label text.

**Files changed:** `Toucan/Views/Components/ModeSelectorBar.xaml`

### Requirement 2: Multi-Line Editing

**Current state:** The `ui:TextBox` in `TranslationItemView.xaml` already has `AcceptsReturn="True"`, `TextWrapping="Wrap"`, and `MinHeight="28"`. Requirements 2.1, 2.2, and 2.5 are already satisfied.

**Change:** Add `MaxHeight="200"` and `VerticalScrollBarVisibility="Auto"` to cap vertical growth (Req 2.3). The existing `IsReadOnly` DataTrigger for Audit mode already satisfies Req 2.4.

```xml
<ui:TextBox
    ...
    MaxHeight="200"
    VerticalScrollBarVisibility="Auto"
    ... />
```

**Files changed:** `Toucan/Views/Components/TranslationItemView.xaml`

### Requirement 3: Review/Audit Action Buttons

**Current state:** An approve `ToggleButton` already exists in the language row (Grid.Column="4"), visible only in Review mode. It binds `IsChecked="{Binding IsApproved, Mode=TwoWay}"` and has visual triggers for approved/unapproved states. Requirements 3.3, 3.5, 3.6 are already satisfied.

**Changes needed:**

1. **Comment button** (Req 3.1, 3.2): Add a `ui:Button` in a new Grid.Column between Copy and Approve, visible in Review AND Audit modes. Command binds to a new `ViewCommentCommand` on `TranslationItemViewModel`.
2. **Needs-review button** (Req 3.4): Add a second small button that clears approval. Command binds to a new `ClearApprovedCommand` on `TranslationItemViewModel` (sets `IsApproved = false`).

**New ViewModel members on `TranslationItemViewModel`:**

```csharp
[RelayCommand]
private void ViewComment()
{
    // ponytail: for now, copies comment to clipboard; upgrade path: open comment flyout
    if (!string.IsNullOrEmpty(Comment))
        Clipboard.SetText(Comment);
}

[RelayCommand]
private void ClearApproved() => IsApproved = false;
```

**XAML layout change** — expand Grid columns in the language row:

```
Col 0: Language label (32)
Col 1: Spacer (8)
Col 2: TextBox (*)
Col 3: Copy button (Auto, hover-only)
Col 4: Comment button (Auto, Review+Audit only)
Col 5: Needs-review button (Auto, Review only)
Col 6: Approve toggle (Auto, Review only)
```

**Files changed:** `Toucan/Views/Components/TranslationItemView.xaml`, `Toucan/ViewModels/TranslationItemViewModel.cs`

### Requirement 4: Language Visibility Checkboxes

**Design choice — filtering approach:** Use a `Visibility` binding on each Language_Row inside the DataTemplate, driven by a lookup into a dictionary of visible languages on the ViewModel. This avoids rebuilding the `Translations` ObservableCollection (which would break focus/selection state). The filter lives on `MainWindowViewModel` as a session-scoped dictionary.

**New types:**

```csharp
// In MainWindowViewModel (new partial or in Nav partial)
[ObservableProperty]
private ObservableCollection<LanguageVisibilityItem> languageVisibilityFilter = [];

// Simple bindable item
public partial class LanguageVisibilityItem : ObservableObject
{
    [ObservableProperty] private string language = string.Empty;
    [ObservableProperty] private bool isVisible = true;
}
```

**Initialization:** When a project loads (in the existing `ProjectChanged` handler), populate `LanguageVisibilityFilter` with one item per distinct language, all `IsVisible = true`.

**Row visibility:** Each Language_Row's `Visibility` binds to a multi-binding or uses a converter that checks whether `Language` is in the visible set. Simplest: bind the row's `Visibility` to the `TranslationItemViewModel.Language` via an `IMultiValueConverter` that looks up the language in a static/singleton visible-language set.

For simplicity and minimal diff, the approach is:

1. `MainWindowViewModel` exposes a `HashSet<string> VisibleLanguagesSet` (not observable, recalculated when checkboxes change).
2. `MainWindowViewModel` exposes an `int LanguageVisibilityVersion` (`[ObservableProperty]`) that increments on any checkbox toggle — this triggers re-render.
3. In the `TranslationItemView.xaml` DataTemplate, each `LangRow` Grid binds its `Visibility` to a converter that checks the language against the set. The converter takes `Language` + `LanguageVisibilityVersion` as inputs (the version forces re-evaluation).

**Alternative (chosen — simpler):** Since `LanguageGroupViewModel.LoadTranslations` already filters by primary language ordering, add a `IsLanguageVisible(string lang)` method on the ViewModel, and have each row's Visibility use a DataTrigger on a computed `IsVisible` property on `TranslationItemViewModel`.

**Final approach:** Add `[ObservableProperty] private bool isLanguageVisible = true;` to `TranslationItemViewModel`. When `LanguageVisibilityFilter` changes, iterate all displayed groups and set `IsLanguageVisible` on each translation VM. Bind row `Visibility="{Binding IsLanguageVisible, Converter={StaticResource BoolToVis}}"`.

**UI placement:** In `TranslationDetailsView.xaml`, add an `ItemsControl` with horizontal `WrapPanel` above the existing filter TextBox, inside the same Border header area.

```xml
<!-- Language visibility checkboxes -->
<ItemsControl ItemsSource="{Binding LanguageVisibilityFilter}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal"/>
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <CheckBox Content="{Binding Language}" IsChecked="{Binding IsVisible, Mode=TwoWay}"
                      Margin="0,0,8,0" FontSize="11" VerticalAlignment="Center"/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Persistence:** Session-only (stored in memory on `MainWindowViewModel`). Toggling pages calls `PagedUpdates()` which re-applies visibility. Req 4.6 satisfied by keeping the filter collection alive for the session.

**Files changed:** `Toucan/ViewModels/TranslationItemViewModel.cs`, `Toucan/ViewModels/MainWindowViewModel.Nav.cs`, `Toucan/Views/Components/TranslationDetailsView.xaml`

### Requirement 5: Inspector Panel Suggestion Insertion

**Data flow:**

```
User focuses a TextBox in TranslationItemView
    → TranslationItemView.GotFocus handler sets MainWindowViewModel.FocusedTranslationItem
    
User clicks Insert on a suggestion in InspectorView
    → InsertSuggestionCommand(string suggestionText) fires
    → If FocusedTranslationItem != null: set Value = suggestionText
    → Else: StatusBarService.Instance.UpdateStatus("No target field selected")
```

**New ViewModel members on `MainWindowViewModel`:**

```csharp
[ObservableProperty]
private TranslationItemViewModel? focusedTranslationItem;

[RelayCommand]
private void InsertSuggestion(string? suggestion)
{
    if (string.IsNullOrEmpty(suggestion)) return;
    if (FocusedTranslationItem == null)
    {
        StatusBarService.Instance.UpdateStatus("No target field selected");
        return;
    }
    FocusedTranslationItem.Value = suggestion;
}
```

**Tracking focus:** In `TranslationItemView.xaml.cs`, handle `GotFocus` on the value TextBox to set `FocusedTranslationItem` on the MainWindowViewModel (accessed via `DataContext` chain or a static reference).

**InspectorView XAML change:** Add an insert button inside the suggestion item DataTemplate:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>  <!-- Icon -->
        <ColumnDefinition Width="*"/>     <!-- Text -->
        <ColumnDefinition Width="Auto"/>  <!-- Insert button -->
    </Grid.ColumnDefinitions>
    ...existing icon + text...
    <ui:Button Grid.Column="2" Padding="4" Appearance="Transparent"
               Command="{Binding DataContext.InsertSuggestionCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
               CommandParameter="{Binding}"
               ToolTip="Insert suggestion"
               AutomationProperties.Name="Insert suggestion">
        <!-- Hide in Audit mode -->
        <ui:Button.Style>
            <Style TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}">
                <Setter Property="Visibility" Value="Visible"/>
                <Style.Triggers>
                    <DataTrigger Binding="{Binding EditorMode, Source={x:Static services:PanelService.Instance}}" 
                                 Value="{x:Static vm:EditorMode.Audit}">
                        <Setter Property="Visibility" Value="Collapsed"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </ui:Button.Style>
        <ui:SymbolIcon FontSize="12" Symbol="ArrowImport16"/>
    </ui:Button>
</Grid>
```

**Files changed:** `Toucan/ViewModels/MainWindowViewModel.Nav.cs` (or a new `MainWindowViewModel.Edit.cs` partial if it exists), `Toucan/Views/Components/InspectorView.xaml`, `Toucan/Views/Components/TranslationItemView.xaml.cs`

## Data Models

### New: `LanguageVisibilityItem`

```csharp
public partial class LanguageVisibilityItem : ObservableObject
{
    [ObservableProperty] private string language = string.Empty;
    [ObservableProperty] private bool isVisible = true;
}
```

Location: `Toucan/ViewModels/LanguageVisibilityItem.cs` (or inline in `MainWindowViewModel.Nav.cs` if small enough).

### Modified: `TranslationItemViewModel`

New members:
- `[ObservableProperty] private bool isLanguageVisible = true;`
- `[RelayCommand] private void ViewComment()`
- `[RelayCommand] private void ClearApproved()`

### Modified: `MainWindowViewModel`

New members:
- `ObservableCollection<LanguageVisibilityItem> LanguageVisibilityFilter`
- `TranslationItemViewModel? FocusedTranslationItem`
- `InsertSuggestionCommand`
- Helper method to apply language visibility to displayed groups

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Language visibility filter count matches project languages

*For any* set of project languages, the `LanguageVisibilityFilter` collection SHALL contain exactly one `LanguageVisibilityItem` per distinct language, with all `IsVisible` initialized to `true`.

**Validates: Requirements 4.2, 4.5**

### Property 2: Language visibility filtering correctness

*For any* set of language visibility states (some checked, some unchecked) and any `LanguageGroupViewModel` with translations across multiple languages, the visible language rows SHALL be exactly those whose language code appears in the set of checked languages.

**Validates: Requirements 4.3, 4.4**

### Property 3: Suggestion insertion replaces value and marks dirty

*For any* non-empty suggestion string and any focused `TranslationItemViewModel`, calling `InsertSuggestion` SHALL set the item's `Value` to the suggestion string and the item SHALL be marked dirty (triggering save-debounce).

**Validates: Requirements 5.2, 5.4**

## Error Handling

| Scenario | Handling |
|----------|----------|
| Insert suggestion with no focused item | Display status bar message "No target field selected" (Req 5.3) |
| Project has 0 languages | `LanguageVisibilityFilter` is empty; no checkbox row rendered |
| Comment is empty when ViewComment clicked | No-op (button still visible for adding comments in future) |
| DropShadowEffect not supported on GPU | WPF degrades gracefully to software rendering; no action needed |

## Testing Strategy

**Property-based tests (FsCheck, xUnit v3):**
- Property 1: Generate random language lists (1–20 languages), initialize the filter, assert count and all-visible invariant.
- Property 2: Generate random visibility masks, apply to a `LanguageGroupViewModel` with N translations, assert visible set matches.
- Property 3: Generate random suggestion strings, create a `TranslationItemViewModel`, insert, assert value equality.

Each property test runs minimum 100 iterations. Tag format: `Feature: editor-ux-improvements, Property N: <title>`.

**Unit tests (example-based):**
- Mode switching triggers correct PropertyChanged notifications
- Approve/ClearApproved toggles IsApproved correctly
- FocusedTranslationItem null guard shows status message
- Language visibility persists across page navigation

**Manual verification:**
- Visual appearance of active pill shadow (Req 1.3)
- TextBox vertical scroll activates at 200px (Req 2.3)
- Comment/approve buttons show/hide per mode (Req 3.1–3.4)

## File Change Summary

| File | Change |
|------|--------|
| `Toucan/Views/Components/ModeSelectorBar.xaml` | Add elevation effect + MinWidth on active pill |
| `Toucan/Views/Components/TranslationItemView.xaml` | Add MaxHeight, comment button, needs-review button, isLanguageVisible binding |
| `Toucan/Views/Components/TranslationDetailsView.xaml` | Add language visibility checkbox row |
| `Toucan/Views/Components/InspectorView.xaml` | Add insert button per suggestion |
| `Toucan/ViewModels/TranslationItemViewModel.cs` | Add IsLanguageVisible, ViewComment, ClearApproved |
| `Toucan/ViewModels/MainWindowViewModel.Nav.cs` | Add LanguageVisibilityFilter, FocusedTranslationItem, InsertSuggestionCommand, visibility helpers |
| `Toucan/Views/Components/TranslationItemView.xaml.cs` | Handle TextBox GotFocus to track focused item |
| `tests/Toucan.Tests/` | Property tests for visibility filter and suggestion insertion |
