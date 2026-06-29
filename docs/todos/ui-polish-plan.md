# RC Phase 4: UX & UI Polish Plan

> Goal: Standardize all dialogs/windows, extract reusable components, reduce repetition.
> All windows already use `ui:FluentWindow` ✅ — focus is on internal consistency.

---

## Current State Audit

| File | Lines | Issue |
|------|-------|-------|
| LanguagesView.xaml | 412 | Largest component — inline templates, repeated button styles |
| OptionsDialog.xaml | 304 | 7 pages inline — each page should be a UserControl |
| PreTranslateWindow.xaml | 252 | Complex layout, repeated progress/result patterns |
| MainMenu.xaml | 225 | Long menu tree — could extract submenus |
| ProviderSettingsWindow.xaml | 206 | Repeated card+textbox patterns for each provider field |
| ManageLanguagesDialog.xaml | 203 | Inline templates for language items |
| ToolBarView.xaml | 176 | OK — toolbar buttons are inherently repetitive |
| NewProjectPromp.xaml | 172 | 2-step wizard inline — each step should be a component |

**Repeated patterns across dialogs:**
- `ui:TitleBar` (15 instances) — all identical with minor title differences
- `ui:CardControl` (36 instances) — same Header+Content structure everywhere
- Footer button bar (OK/Cancel, right-aligned) — repeated in 8+ dialogs
- Panel header (title + action buttons in border) — repeated in 4 components

---

## Plan

### 1. Extract Shared Components

#### `DialogFooter.xaml` (UserControl)
Replaces the repeated OK/Cancel footer bar pattern:
```xml
<UserControl>
  <StackPanel FlowDirection="RightToLeft" Orientation="Horizontal" Margin="12,8">
    <ui:Button Content="{Binding CancelText}" Command="{Binding CancelCommand}"/>
    <ui:Button Content="{Binding OkText}" Appearance="Primary" Command="{Binding OkCommand}"/>
  </StackPanel>
</UserControl>
```
Used in: OptionsDialog, ImportProject, NewProject, LanguagePrompt, PreTranslate, ProviderSettings, ManageLanguages, Statistics

#### `SettingsCard.xaml` (UserControl)
Replaces repeated `ui:CardControl` with Header/Description/Content pattern:
```xml
<UserControl>
  <ui:CardControl>
    <ui:CardControl.Header>
      <StackPanel>
        <TextBlock FontWeight="SemiBold" Text="{Binding Title}"/>
        <TextBlock FontSize="11" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Text="{Binding Description}"/>
      </StackPanel>
    </ui:CardControl.Header>
    <ContentPresenter Content="{Binding Content}"/>
  </ui:CardControl>
</UserControl>
```
Used in: OptionsDialog (10x), ProviderSettings (6x), NewProject (3x)

#### `PanelHeader.xaml` (UserControl)
Replaces repeated panel title bar (title + action buttons in subtle background):
```xml
<Border Padding="8,5" Background="{DynamicResource SubtleFillColorTertiaryBrush}" CornerRadius="6,6,0,0">
  <Grid>
    <TextBlock FontSize="12" FontWeight="SemiBold" Text="{Binding Title}"/>
    <ContentPresenter Content="{Binding Actions}" HorizontalAlignment="Right"/>
  </Grid>
</Border>
```
Used in: LanguagesView, ResourcesView, TranslationDetailsView, StatusBarView

#### `LanguageChip.xaml` (UserControl)
Replaces repeated language badge rendering:
```xml
<Border CornerRadius="4" Padding="8,4" Background="{DynamicResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{DynamicResource ControlElevationBorderBrush}" BorderThickness="1">
  <TextBlock Text="{Binding}" FontSize="12"/>
</Border>
```
Used in: ImportProjectDialog, NewProject, ManageLanguages, LanguagesView

### 2. Split Large Files

#### OptionsDialog → Page UserControls
Split 7 inline pages into separate UserControls:
- `Views/Settings/GeneralSettingsPage.xaml`
- `Views/Settings/EditorSettingsPage.xaml`
- `Views/Settings/LanguagesSettingsPage.xaml`
- `Views/Settings/TranslationSettingsPage.xaml`
- `Views/Settings/ShortcutsSettingsPage.xaml`
- `Views/Settings/ProjectSettingsPage.xaml`
- `Views/Settings/AboutPage.xaml`

OptionsDialog.xaml becomes ~50 lines (nav list + ContentPresenter).

#### NewProjectPrompt → Step UserControls
- `Views/NewProject/FrameworkStep.xaml` (tile grid + name/folder)
- `Views/NewProject/LanguagesStep.xaml` (language list + add/remove)

#### LanguagesView → Extract Inline Templates
- `Views/Components/LanguageGroupCard.xaml` — the expandable group per language
- `Views/Components/TranslationRow.xaml` — single key/value edit row

### 3. Dialog Standardization Rules

Every dialog MUST follow this structure:
```xml
<ui:FluentWindow ExtendsContentIntoTitleBar="True" WindowBackdropType="Mica" WindowCornerPreference="Round">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>  <!-- TitleBar -->
      <RowDefinition Height="*"/>     <!-- Content -->
      <RowDefinition Height="52"/>    <!-- Footer (if has OK/Cancel) -->
    </Grid.RowDefinitions>
    
    <ui:TitleBar Grid.Row="0" Title="..." ShowMaximize="False" ShowMinimize="False"/>
    
    <!-- Content area -->
    <ScrollViewer Grid.Row="1" Padding="24,16">...</ScrollViewer>
    
    <!-- Footer (use DialogFooter component) -->
    <local:DialogFooter Grid.Row="2"/>
  </Grid>
</ui:FluentWindow>
```

Properties all dialogs MUST set:
- `ExtendsContentIntoTitleBar="True"`
- `WindowBackdropType="Mica"`
- `WindowCornerPreference="Round"`
- `WindowStartupLocation="CenterOwner"`
- `ShowMaximize="False"` `ShowMinimize="False"` on TitleBar (for dialogs)
- Min width/height set appropriately

### 4. Design Token Standardization

Create/verify in `Resources/DesignTokens.xaml`:
```xml
<!-- Spacing -->
<Thickness x:Key="DialogPadding">24,16</Thickness>
<Thickness x:Key="CardSpacing">0,0,0,4</Thickness>
<Thickness x:Key="SectionSpacing">0,16,0,0</Thickness>
<Thickness x:Key="FooterMargin">12,8</Thickness>
<Thickness x:Key="ToolbarButtonMargin">2,0</Thickness>
<Thickness x:Key="ToolbarButtonPadding">6,4</Thickness>

<!-- Typography -->
<sys:Double x:Key="PageTitleFontSize">24</sys:Double>
<sys:Double x:Key="SectionTitleFontSize">14</sys:Double>
<sys:Double x:Key="CaptionFontSize">11</sys:Double>
```

### 5. Accessibility & Keyboard

- All buttons: `ToolTip` + `AutomationProperties.Name`
- All dialogs: Tab order verified, Escape closes, Enter confirms
- All lists: keyboard navigation (Up/Down/Enter)
- Focus management: first input auto-focused on dialog open

---

## Execution Order

1. **Extract `DialogFooter`** — smallest, highest reuse (8 dialogs)
2. **Extract `SettingsCard`** — reduces OptionsDialog significantly
3. **Extract `PanelHeader`** — used in 4 components
4. **Split OptionsDialog into page UserControls**
5. **Split NewProjectPrompt into step UserControls**
6. **Extract `LanguageChip`** — used in 4 places
7. **Verify design tokens** — ensure all spacing/typography uses shared resources
8. **Accessibility pass** — tooltips, tab order, automation names

---

## Metrics

| Metric | Before | Target |
|--------|--------|--------|
| Largest XAML file | 412 lines | < 150 lines |
| Repeated patterns | 36 CardControls, 15 TitleBars | Extracted to components |
| OptionsDialog size | 304 lines | ~50 lines (shell + pages) |
| Custom components | 9 | 15+ |
| Design token coverage | Partial | 100% (no magic numbers) |
