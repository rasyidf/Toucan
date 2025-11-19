# New Project Dialog — TODO

Saved: 2025-11-19

This file contains the current action items for the New Project Dialog feature. We'll review this list at our next meeting.

## Tasks

1. Open new project UI and viewmodel — completed ✅
   - Read `Views/NewProjectPromp.xaml` and `Views/NewProjectPromp.xaml.cs`
   - Confirmed a matching ViewModel is needed and created a skeleton

2. Design XAML for dialog — completed ✅
   - Added framework selection (JSON/INI/PO/YAML)
   - Added project folder browser
   - Added language management controls and packages list

3. Wire-up code-behind and viewmodel — completed ✅
   - `ViewModels/NewProjectViewModel.cs` added
   - `TranslationPackage` and `TranslationEntry` types added
   - Browsing for folders/files wired via `DialogService`
   - Validation and `OK` button gating implemented

4. Add multi-framework initial file support — in-progress ⚙️
   - Goal: allow initial project creation for PO, INI, YAML as well as JSON
   - Next: implement SaveStrategies / file creation in `Toucan.Core.Services.ProjectService` or a new `CreateLanguage` overload

5. Move `LanguagePrompt` invocation to view — not-started ⏸️
   - For stricter MVVM, keep UI navigation code in the code-behind and keep the ViewModel testable

6. Improve validation & UI polish — not-started ⏸️
   - Inline validation messages and disabling Create button until all required fields set
   - Tighten layout and style to match design mockups

7. Add unit & integration tests — not-started ⏸️
   - Unit tests for `NewProjectViewModel` validation
   - Integration tests for multi-framework file generation

## Meeting notes / next steps

- Please confirm whether initial language files for INI/PO/YAML should be created using the save strategies or if a separate creation flow is preferred.
- After that decision, I'll implement those strategies and add tests.

---

File created so we don't lose progress. See you at the meeting!