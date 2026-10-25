---
title: "Windows UI review"
status: active
updated: 2026-10-21
summary: "Windows dialog surface defects, responsive welcome layout, screenshot coverage and remaining validation."
---
# Windows UI review — 21 October 2026

The Windows UI had real surface and theme defects. It should not have been considered ready based only on the macOS and Linux results. The working tree already contained a partial dialog backplate fix when this review started; that fix still produced white dialog bodies with white headings in dark mode.

## Fixed in this review

| Finding | Effect | Change and evidence |
|---|---|---|
| Dialog background lookup returned null | The previous constructor queried application resources without a window theme context. Its fallback backplate was white, including in dark mode. | Resolve the background through the window's resource observable. The regression test failed with a null brush before the change and passes afterward. Compare `build/ui-review/before/32-dark-new-project.png` with `build/ui-review/after/dark/20-new-project.png`. |
| Dialog backgrounds did not follow theme changes | A dialog could retain its old surface while child controls changed theme. | Bind both the window and backplate to the current background. Test switches Light → Dark while the dialog remains open and checks both brushes. |
| Border-root dialogs skipped the shared backplate | Protection depended on the shape of each dialog's content root. | Paint behind every root, retain `WindowTransparencyLevel.None`, and verify opaque rendered pixels with transparent Grid and Border roots. |
| Welcome screen collapsed at minimum width | Narrow columns clipped action labels and wrapped the introductory description into an almost vertical strip. | Stack the welcome/recent sections and secondary sections below a width threshold; use three template columns. Compare `build/ui-review/before` baseline with `after/light/01-start-screen-minimum.png`. |
| Static color swatches could belong to another thread | Shared navigation and preset brushes could cause rendering failures when first initialized by a worker. | Use immutable brushes for shared swatches. Run settings tests that access UI resources on the Avalonia dispatcher. |
| UI test collections raced shared application state | Full-suite runs could fail despite isolated screenshot runs succeeding. | Serialize the UI suite; retain all assertions. The full regression run passed all 243 cases, including the project-creation interaction tests. |
| Missing Indonesian label | The new welcome screen failed the translation completeness check. | Add “View all” → “Lihat semua”. |
| Bright illustration background and redundant welcome navigation | The artwork retained a white rectangle in Dark, and a second sidebar offered unnecessary section links. | Use a transparent illustration on a theme-aware gray card, remove the welcome navigation, and retain the app activity rail and functional page actions. |
| Inconsistent template shortcuts and wizard organization | Welcome shortcuts used unrelated icons and unequal widths; the wizard separated scrolling templates from fixed project fields. | Share framework cards and template data between both views, use equal grid cells, place aligned project fields above the framework section, and scroll the wizard body as one surface above a fixed footer. Shortcuts preselect the chosen framework. Interaction tests verify template routing and short-window navigation. |

Existing welcome/import work and the supplied illustration were preserved.

## Evidence and coverage

- **140 screenshots:** 70 views/states in Light and the same 70 in Dark. Open [the searchable gallery](../build/ui-review/after/index.html).
- Every application dialog class is represented: Settings, New Project, Import Project, Project Properties, Statistics, Manage Languages, Provider Settings, text prompt, picker, language prompt, onboarding, prompt editor, pre-translation and plugin dialog host.
- All 14 Settings pages, all five Project Properties pages, both wizard steps, empty/detected import states, empty/populated plugin states, AI off/on, and Settings search match/no-match states.
- Start screen, editor, eight side panels, Review, Audit, Zen, multiselect, hidden namespaces, command palette, shortcuts and translation suggestion text.
- Main window at 1280×800 and 880×560; Settings at 720×480; new-project wizard at its minimum height.
- File menu captured separately from its owner; information, confirmation and unsaved-changes overlays captured inside their owner.
- Native Windows desktop checks: start screen, new-project dialog and import dialog. Their observed surfaces were opaque and readable. These were spot checks, not 140 native compositor screenshots.
- Screenshot fixtures do not call external translation providers; provider failures, live service responses and arbitrary third-party plugin content are not exhaustively covered.

## Remaining improvements, in priority order

| Priority | Observation or gap | Follow-up |
|---|---|---|
| P1 | After resizing an open editor to minimum width, the screenshot shows the right caption controls clipped; the existing shell test constructs the window at each size. | Add a wide → narrow → wide resize test that checks all caption buttons and menu overflow. Verify the same sequence natively before release. |
| P1 | The Light text-prompt capture does not paint its prefilled value, while Dark does. | Check the native rename workflow and compare `TextBox.Text` with painted output; distinguish a headless capture issue from a product defect before changing prompt behavior. |
| P1 | Native scaling, compositor settings and monitor changes are outside this screenshot matrix. | Check 100%, 125%, 150% and 200% display scaling, maximize/restore, monitor movement, system Light/Dark changes and Windows transparency-effects settings. |
| P2 | The welcome screen includes English strings outside localization markup. | Localize the new navigation, explanatory copy and section titles; capture Indonesian and a long-label locale. The current completeness test checks marked strings only. |
| P2 | Some dialog views have large empty regions; populated Plugins is captured at an artificial 2400px height. | Add normal-height populated states and populated Statistics/Manage Languages/pre-translation results; verify scrolling and footer visibility without oversized fixtures. |
| P2 | The current menu image covers File only. | Extend native checks to explorer context menus, language/provider dropdowns, notification flyouts, tooltips, color picker and OS file/folder/save dialogs. |
| P3 | Small captions and faint borders look weak in several light-theme screens. | Review measured contrast and keyboard focus at common Windows scaling settings. |

This review fixes the reproduced background and welcome-layout defects. It does **not** establish that every Windows configuration or transient UI state is release-ready. macOS and Linux were not rerun on this Windows host.

## Reproduce

Follow [visual-review.md](visual-review.md). Regression results and screenshot-run results live under `build/ui-review/`; images and generated HTML are local build artifacts rather than checked-in baselines.
