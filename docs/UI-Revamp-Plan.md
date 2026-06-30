# Toucan UI Revamp Plan

## Goal

Restructure the WPF app layout into a VS Code-style three-pane hierarchical browser with proper panel management, three editor modes, and working zen/fullscreen support. This is a **layout and interaction revamp** — no WinUI migration.

---

## Current State (Problems)

1. **PanelService is dead code** — properties exist but MainWindow.xaml has zero bindings to them. Zen mode toggles flags that nothing reads.
2. **Two-pane only** — left sidebar (tree + languages) and center editor. No right contextual pane.
3. **No distinct editor modes** — FocusedEditorMode exists in the VM but isn't surfaced as a coherent UX mode.
4. **Zen mode is non-functional** — PanelService.EnterZenMode() hides flags, but XAML never collapses the corresponding elements.
5. **Fullscreen not implemented** — no WindowState toggle, no F11 binding.
6. **Sidebar space conflict** — ResourcesView and LanguagesView compete for vertical space in a fixed 320px column.
7. **No mode indicator** — user has no idea which mode they're in.

---

## Target Layout

```
┌─────────────────────────────────────────────────────────────┐
│  TitleBar (logo + MenuBar + mode indicator + search)        │
├─────────────────────────────────────────────────────────────┤
│  ToolBar (collapsible, hidden in Zen)                       │
├───────────┬───────────────────────────────┬─────────────────┤
│  LEFT     │  CENTER                       │  RIGHT          │
│  SIDEBAR  │  EDITOR AREA                  │  INSPECTOR      │
│  (240–320)│  (flexible)                   │  (280, toggle)  │
│           │                               │                 │
│  Tree/    │  Breadcrumb / mode header     │  Tab strip:     │
│  List of  │  ──────────────────────       │  Stats │ MT │   │
│  keys     │  Translation cards            │  Details        │
│           │  (paginated or infinite)      │                 │
│           │                               │                 │
├───────────┴───────────────────────────────┴─────────────────┤
│  StatusBar (mode badge, project, progress, cursor)          │
└─────────────────────────────────────────────────────────────┘
```

### Zen Mode (all chrome hidden, center fills window)

```
┌─────────────────────────────────────────────────────────────┐
│  [Esc to exit]          Translation cards          [n/N]    │
│                         (single-item or paged)              │
└─────────────────────────────────────────────────────────────┘
```

### Fullscreen = WindowState.Maximized + hide taskbar title (F11 toggle)

---

## Three Editor Modes

| Mode | Purpose | Center pane shows | Right pane default |
|------|---------|-------------------|--------------------|
| **Editor** (default) | Translate | All language values per key, editable | MT suggestions + TM |
| **Review** | Verify translations | Source + target side-by-side, approve/reject buttons prominent | Comments + validation warnings |
| **Audit** | QA / sign-off | Read-only values + change history + approval state | Audit log + placeholder validation |

### Mode switching

- Toolbar `SelectorBar` (3 icons: ✏️ Edit, 👁️ Review, ✓ Audit)
- Keyboard: `Ctrl+1` / `Ctrl+2` / `Ctrl+3`
- Mode stored in VM as `EditorMode` enum, persisted in layout.json
- Each mode can specify which right-pane tab is active by default

### Mode-specific behavior

**Editor mode:**
- TextBoxes editable, two-way bound
- Dirty-state tracking active
- Right pane: Suggestions tab (MT + TM), Details tab (comments, source usages)

**Review mode:**
- Source language displayed as reference (read-only)
- Target language editable with approve/reject toggle
- Filter defaults to "Needs Review" (untranslated + unapproved)
- Right pane: Validation tab (placeholder mismatches, whitespace issues), Comments tab

**Audit mode:**
- All values read-only
- Shows approval state, last editor, timestamp per key
- Bulk approve/reject actions available
- Right pane: Audit history tab, Statistics tab

---

## Implementation Tasks

### Phase 1: Wire PanelService to XAML (fix what's broken)

1. **Bind panel visibility** — Add `Visibility` bindings on ResourcesView, LanguagesView, ToolBarView, StatusBarView to `PanelService.Instance.*Visible` properties (via a static resource or DataContext path).
2. **Bind sidebar column width** — Bind `ColumnDefinition.Width` to `PanelService.SidebarWidth` so splitter changes persist.
3. **Zen mode works** — After step 1, toggling ZenMode will actually hide panels.
4. **Fullscreen toggle** — Add `ToggleFullscreen` command: swap `WindowState` between `Maximized`/`Normal`, optionally hide title bar chrome. Bind to F11.
5. **Save layout on close** — Call `PanelService.SaveLayout()` in Window.Closing.

### Phase 2: Three-pane layout

6. **Add right pane column** — Change content grid from 3 columns (`Left | Splitter | Center`) to 5 columns (`Left | Splitter | Center | Splitter | Right`).
7. **Right pane UserControl: InspectorView** — Contains a `TabControl` with tabs: Stats, Suggestions, Details, Validation, Audit.
8. **Move LanguagesView into InspectorView.Stats tab** — Left sidebar becomes tree-only (simpler, more vertical space for keys).
9. **Right pane toggle** — Collapsible via `PanelService.InspectorVisible`. Toolbar button + `Ctrl+Shift+I`.
10. **Context-sensitive tab selection** — When a key is selected, right pane updates to show relevant info for that key.

### Phase 3: Editor modes

11. **`EditorMode` enum** — `Editor`, `Review`, `Audit` in ViewModel.
12. **Mode SelectorBar in toolbar** — Three-segment control bound to `EditorMode`.
13. **Mode-aware TranslationItemView** — DataTemplateSelector or Visibility bindings that show/hide edit controls, approve buttons, audit info based on mode.
14. **Review mode filter preset** — Switching to Review mode auto-applies "needs review" filter (unapproved + non-empty). Switching back restores previous filter.
15. **Audit mode read-only** — TextBox.IsReadOnly bound to `EditorMode == Audit`. Show AuditService metadata inline.
16. **Right pane tab auto-switch** — Editor→Suggestions, Review→Validation, Audit→AuditHistory.
17. **Mode badge in StatusBar** — Shows current mode name/icon.
18. **Persist mode** — Save/restore active mode in layout.json.

### Phase 4: Zen mode & focused editing (proper)

19. **Zen mode overlay** — Instead of just hiding panels, overlay a dedicated ZenEditorView on top of the main grid. Shows a single translation group at a time (or paged, configurable).
20. **Zen navigation** — `↓`/`↑` or `J`/`K` to move between items. `Esc` exits. Subtle hint text at top edge.
21. **Zen + mode** — Zen respects the current EditorMode (edit in zen, review in zen, audit in zen).
22. **Focused language subset** — Already exists (`FocusedLanguages`). Surface it in Zen mode UI as a compact language picker at top.

### Phase 5: Performance

23. **Virtualize translation list** — Replace `ItemsControl` + `ScrollViewer` with `VirtualizingStackPanel` (or switch to `ListView` with virtualization). Current approach creates all item UIs upfront per page.
24. **Reduce binding converter allocations** — Cache converter instances as static resources. Remove redundant converters where a DataTrigger suffices.
25. **Deferred right pane loading** — Only instantiate the active tab's content. Use `ContentControl` + `DataTemplate` selection (lazy).
26. **Throttle tree rebuild** — When namespace filter changes, debounce tree refresh (already 300ms for search; ensure tree doesn't full-rebuild on every keystroke).
27. **Pagination default** — Keep pagination (already exists). Infinite scroll should warn if >500 items.

---

## File Changes Summary

| File | Change |
|------|--------|
| `MainWindow.xaml` | Add right column, bind all Visibility to PanelService, add Zen overlay, fullscreen command |
| `PanelService.cs` | Add `InspectorVisible`, `EditorMode`, persist mode |
| `MainWindowViewModel.Nav.cs` | Add `EditorMode` property, mode-switch commands, fullscreen command |
| `Views/Components/InspectorView.xaml` (new) | Right pane with TabControl |
| `Views/Components/TranslationItemView.xaml` | Mode-aware template (edit/review/audit states) |
| `Views/Components/ZenEditorView.xaml` (new) | Fullscreen overlay for zen mode |
| `Views/Components/ModeSelectorBar.xaml` (new) | Three-segment mode switcher |
| `ViewModels/InspectorViewModel.cs` (new) | Drives right pane tab content |
| `Services/KeybindingService.cs` | Add F11, Ctrl+1/2/3 bindings |
| `StatusBarView.xaml` | Add mode badge |
| `ToolBarView.xaml` | Add mode selector, inspector toggle |

---

## Out of Scope

- WinUI migration (planned separately)
- New translation providers
- Translation memory implementation
- Import/export features
- The Avalonia target (focus on WPF only)

---

## Design Constraints

- Stay within WPF + WPF UI (Lepo) library — no new UI frameworks
- Keep CommunityToolkit.Mvvm patterns (ObservableProperty, RelayCommand)
- PanelService remains singleton until DI migration
- Layout persistence format (layout.json) extended, not replaced
- Existing keybinding service extended with new bindings
