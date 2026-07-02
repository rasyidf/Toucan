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

### Phase 1: Wire PanelService to XAML ✅ DONE

1. ✅ Bind panel visibility — Visibility bindings on all panels to PanelService.Instance.*Visible.
2. ✅ Bind sidebar column width — SidebarWidth persisted via PanelService.
3. ✅ Zen mode works — ZenEditorView overlay with PanelService sync.
4. ✅ Fullscreen toggle — F11 bound to ToggleFullscreenCommand.
5. ✅ Save layout on close — PanelService.SaveLayout() called in Window.Closing.

### Phase 2: Three-pane layout ✅ DONE

6. ✅ 5-column grid — Left | Splitter | Center | Splitter | Right.
7. ✅ InspectorView — TabControl with Stats, Suggestions, Details, Validation tabs.
8. ✅ LanguagesView in Inspector.Stats tab — Left sidebar is tree-only.
9. ✅ Right pane toggle — PanelService.InspectorVisible, Ctrl+Shift+I, toolbar button.
10. ✅ Context-sensitive tab selection — Auto-switches per EditorMode.

### Phase 3: Editor modes ✅ DONE

11. ✅ EditorMode enum — Editor, Review, Audit.
12. ✅ ModeSelectorBar in toolbar — Segmented control with Ctrl+Alt+1/2/3.
13. ✅ Mode-aware TranslationItemView — Audit=read-only, Review=approve toggle, Editor=translate button.
14. ✅ Review mode filter preset — Auto-applies unapproved/untranslated filter; restores on exit.
15. ✅ Audit mode read-only — TextBox.IsReadOnly via DataTrigger on EditorMode.
16. ✅ Right pane tab auto-switch — Editor→Suggestions, Review→Validation, Audit→Details.
17. ✅ Mode badge in StatusBar — Pill badge with color per mode.
18. ✅ Persist mode — Saved/restored in layout.json via PanelService.

### Phase 4: Zen mode & focused editing ✅ DONE

19. ✅ Zen mode overlay — ZenEditorView covers all chrome at ZIndex=100.
20. ✅ Zen navigation — J/K/↑/↓ + Esc exit, handled via PreviewKeyDown.
21. ✅ Zen + mode — Badge shows "ZEN · EDITOR/REVIEW/AUDIT".
22. ✅ Focused language subset — Compact language indicator at bottom of ZenEditorView.

### Phase 5: Performance (TODO)

23. [ ] **Virtualize translation list** — Replace `ItemsControl` with `ListView` + `VirtualizingStackPanel`.
24. [ ] **Reduce binding converter allocations** — Cache converter instances as static resources.
25. [ ] **Deferred right pane loading** — Only instantiate the active tab's content (lazy DataTemplate).
26. [ ] **Throttle tree rebuild** — Ensure tree doesn't full-rebuild on every keystroke (already 300ms debounce for search).
27. [ ] **Pagination default** — Keep pagination. Infinite scroll should warn if >500 items.

### Phase 6: Component Extraction ✅ DONE

28. ✅ DialogFooter — Extracted as reusable UserControl.
29. ✅ SettingsCard — Extracted as reusable UserControl.
30. ✅ PanelHeader — Extracted as reusable UserControl.
31. ✅ Split OptionsDialog — 8 page UserControls in Views/Settings/.
32. ✅ Split NewProjectPrompt — FrameworkStep + LanguagesStep in Views/NewProject/.

### Phase 7: Toolbar, Menu & Footer Redesign ✅ DONE

33. ✅ Menu redesigned — segmented contextual menu (File, Edit, Tools, Find, View, Help).
34. ✅ Toolbar redesigned — Snipping Tool-style pill-grouped icon buttons, no Card wrapper, flat border.
35. ✅ Footer action bar — Photos-style (left: quick actions, center: status, right: info panels).
36. ✅ Footer buttons borderless — custom PanelButton template with subtle hover/pressed states.
37. ✅ Mode badge de-duplicated — lives only in toolbar's ModeSelectorBar, removed from footer.
38. ✅ Toolbar hidden on start screen — DataTrigger collapses toolbar when ShowStartScreen=true.
39. ✅ Sidebar/Inspector default width — 200px each (~1:3:1 ratio).
40. ✅ ResourcesView — removed ui:Card wrapper, plain Border for edge-to-edge content.
41. ✅ Start screen transparent — no Background, Mica backdrop shows through.

---

## Status: COMPLETE

All functional UI revamp work is done. Only Phase 5 (Performance) remains as profiling-driven future work — not blocking release.

---

## Remaining Work (Future — Phase 5 Performance)

These are profiling-driven optimizations, not blocking release:
- Virtualization — measure first, only virtualize if page size > ~50 items causes jank.
- Deferred loading — worthwhile if Inspector tabs have heavy content.
- Tree throttle — already 300ms debounce, verify no full-rebuild on filter change.

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
