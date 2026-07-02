# Panel Extension System — v0.16 Plan

> VS Code-style activity bar + extensible panel slots for sidebar and footer.
> Unifies side panels and status bar under the same Register → Render → Persist pattern.

---

## Design Principle

One pattern, two slots, shared contracts. Every panel (sidebar, footer) follows the same lifecycle:
1. Define (`ISidePanel` / `IStatusBarPanel`)
2. Register (`Registry.Register(panel)`)
3. Render (DataTemplate type matching or ContentControl switch)
4. Persist (active state saved in layout.json)

---

## Current State

### StatusBar ✅ Already Extensible
```
IStatusBarPanel → StatusBarPanelBase → StatusBarPanelRegistry
                                         ├── LeftPanels
                                         ├── CenterPanels
                                         └── RightPanels
```
Any feature calls `Registry.Register(new MyPanel())` and it appears. View uses DataTemplate matching.

### Side Panels ❌ Hardcoded
- Left: ResourcesView only (fixed in XAML)
- Right: InspectorView only (fixed in XAML)
- No activity bar, no panel switching

---

## Target Architecture

```
┌───┬─────────────┬───────────────────────────┬─────────────┬───┐
│ A │  LEFT PANEL │      CENTER EDITOR        │ RIGHT PANEL │ A │
│ c │  (1 active) │                           │ (1 active)  │ c │
│ t │             │                           │             │ t │
│ . │  Explorer   │                           │  Inspector  │ . │
│   │  Source Ctrl│                           │  MT Results │   │
│ B │  Source Code│                           │  Dictionary │ B │
│ a │  Search     │                           │  TM Matches │ a │
│ r │             │                           │             │ r │
└───┴─────────────┴───────────────────────────┴─────────────┴───┘
│  Footer Action Bar (StatusBar — already extensible via registry)  │
└───────────────────────────────────────────────────────────────────┘
```

---

## Core Contracts

### ISidePanel (Toucan.Core/Models/SidePanel.cs)

```csharp
public enum SidePanelSlot { Left, Right }

public interface ISidePanel
{
    string Id { get; }                // "explorer", "source-code", "inspector"
    string Title { get; }             // "Explorer", "Source Code"
    string Icon { get; }              // Fluent symbol: "FolderOpen20", "Code20"
    SidePanelSlot DefaultSlot { get; }
    int Order { get; set; }           // Sort order in activity bar
    bool IsActive { get; set; }       // Currently selected in its slot
    bool IsVisible { get; set; }      // Available (registered)
}

public abstract class SidePanelBase : ObservableObject, ISidePanel { ... }
```

### SidePanelRegistry (Toucan.Core/Services/SidePanelRegistry.cs)

```csharp
public class SidePanelRegistry : ObservableObject
{
    public ObservableCollection<ISidePanel> LeftSlotPanels { get; }
    public ObservableCollection<ISidePanel> RightSlotPanels { get; }

    public ISidePanel? ActiveLeftPanel { get; }
    public ISidePanel? ActiveRightPanel { get; }

    public void Register(ISidePanel panel);
    public void Activate(string panelId);    // Switches active panel in its slot
    public void Unregister(string panelId);
    public void Toggle(string panelId);      // If active, hide slot; if inactive, activate
}
```

---

## Built-in Side Panels

| Id | Title | Icon | Slot | View | Status |
|----|-------|------|------|------|--------|
| `explorer` | Explorer | `FolderOpen20` | Left | ResourcesView (existing) | Migrate |
| `source-code` | Source Code | `Code20` | Left | Key usage list | New |
| `search` | Search | `Search20` | Left | Search/filter with history | New |
| `source-control` | Source Control | `BranchFork20` | Left | Git status | Placeholder |
| `inspector` | Inspector | `Info20` | Right | InspectorView (existing) | Migrate |
| `machine-translation` | Translation | `Translate20` | Right | MT results/progress | New |
| `translation-memory` | Memory | `Library20` | Right | TM matches | New |
| `dictionary` | Dictionary | `Book20` | Right | Glossary/terms | Placeholder |

---

## StatusBar Enhancements (Future, no code changes needed)

The StatusBar is already extensible. Future panels to add:
- `ProgressPanel` — download/upload progress for MT batch jobs
- `DiagnosticsPanel` — validation error/warning count (clickable → opens inspector validation tab)
- `SelectionPanel` — shows selected key count during multi-selection
- `SyncPanel` — sync status when git integration lands

Each is just `Registry.Register(new XPanel())` — zero XAML changes.

---

## XAML Layout (MainWindow.xaml)

```xml
<!-- Current: [Sidebar 200] [Splitter] [Center *] [Splitter] [Inspector 200] -->
<!-- New:     [ActBar 36] [LeftPanel 200] [Split] [Center *] [Split] [RightPanel 200] [ActBar 36] -->

<Grid.ColumnDefinitions>
    <ColumnDefinition Width="36"/>   <!-- Left Activity Bar -->
    <ColumnDefinition Width="200"/>  <!-- Left Panel Content -->
    <ColumnDefinition Width="Auto"/> <!-- Splitter -->
    <ColumnDefinition Width="*"/>    <!-- Center Editor -->
    <ColumnDefinition Width="Auto"/> <!-- Splitter -->
    <ColumnDefinition Width="200"/>  <!-- Right Panel Content -->
    <ColumnDefinition Width="36"/>   <!-- Right Activity Bar -->
</Grid.ColumnDefinitions>
```

### ActivityBar Component

```xml
<views:ActivityBar Slot="Left"
                   Panels="{Binding SidePanelRegistry.LeftSlotPanels}"
                   ActivePanel="{Binding SidePanelRegistry.ActiveLeftPanel}"
                   ActivateCommand="{Binding ActivateLeftPanelCommand}"/>
```

### PanelHost Component

```xml
<views:PanelHost ActivePanel="{Binding SidePanelRegistry.ActiveLeftPanel}"/>
<!-- Uses DataTemplateSelector or Type-mapped ContentControl -->
```

---

## Files to Create

| File | Purpose |
|------|---------|
| `Toucan.Core/Models/SidePanel.cs` | `ISidePanel`, `SidePanelBase`, `SidePanelSlot` enum |
| `Toucan.Core/Services/SidePanelRegistry.cs` | Registry with Activate/Register/Toggle |
| `Toucan/Views/Components/ActivityBar.xaml` + `.cs` | Reusable vertical icon strip |
| `Toucan/Views/Components/PanelHost.xaml` + `.cs` | ContentControl + panel header |
| `Toucan/Views/Panels/ExplorerPanel.xaml` + `.cs` | Wraps ResourcesView |
| `Toucan/Views/Panels/SourceCodePanel.xaml` + `.cs` | Key usage list |
| `Toucan/Views/Panels/SearchPanel.xaml` + `.cs` | Dedicated search |
| `Toucan/Views/Panels/InspectorPanel.xaml` + `.cs` | Wraps InspectorView |
| `Toucan/Views/Panels/MachineTranslationPanel.xaml` + `.cs` | MT results |
| `Toucan/Views/Panels/TranslationMemoryPanel.xaml` + `.cs` | TM matches |
| `Toucan/Views/Panels/SourceControlPanel.xaml` + `.cs` | Placeholder |
| `Toucan/Views/Panels/DictionaryPanel.xaml` + `.cs` | Placeholder |

## Files to Modify

| File | Change |
|------|--------|
| `PanelService.cs` | Integrate `SidePanelRegistry`, add Activate commands |
| `MainWindow.xaml` | Replace hardcoded sidebar with ActivityBar + PanelHost |
| `MainWindow.xaml.cs` | Wire panel content switching, persist active state |
| `App.xaml.cs` | Register built-in side panels |

---

## Execution Phases

| Phase | Scope | Effort | Dependencies |
|-------|-------|--------|--------------|
| **A** | Core contracts (`ISidePanel`, `SidePanelBase`, `SidePanelRegistry`) | S | None |
| **B** | `ActivityBar.xaml` + `PanelHost.xaml` reusable components | S | A |
| **C** | Migrate existing (Explorer wraps ResourcesView, Inspector wraps InspectorView) | M | A, B |
| **D** | MainWindow.xaml layout change (hardcoded → dynamic) | M | B, C |
| **E** | New panel content (SourceCode, Search, MT, TM) | M | D |
| **F** | Placeholder panels (SourceControl, Dictionary) | XS | D |
| **G** | Persist active panel state in layout.json | S | D |

**Parallel opportunities:**
- A and B can run in parallel (B only needs the interface shape, not implementation)
- E and F can run in parallel after D
- G can run after D

---

## What NOT to Change

- Center editor (TranslationDetailsView) — stays as-is
- Footer action bar — stays as-is (already extensible)
- Toolbar — stays as-is
- Zen mode — still hides everything
- Keyboard shortcuts — Ctrl+B toggles left slot, Ctrl+Shift+I toggles right slot

---

## Migration Notes

- ResourcesView stays intact — ExplorerPanel just wraps it
- InspectorView stays intact — InspectorPanel just wraps it
- Existing PanelService visibility booleans become computed from SidePanelRegistry state
- `SidePanelVisible` = left slot has any active panel
- `InspectorVisible` = right slot has any active panel

---

## Future Extension Path (v2.0 Plugin System)

When the plugin system lands, third-party `.dll` plugins can:
```csharp
public class MyPlugin : IToucanPlugin
{
    public void Register(IServiceProvider services)
    {
        var registry = services.GetRequiredService<SidePanelRegistry>();
        registry.Register(new MySidePanel { ... });

        var statusBar = services.GetRequiredService<StatusBarPanelRegistry>();
        statusBar.Register(new MyStatusPanel { ... });
    }
}
```

Zero XAML changes needed — panels self-register and render via templates.
