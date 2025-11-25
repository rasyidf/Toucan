# New Project Dialog — TODO

Last Updated: 2025-11-20

This file contains the current action items for the New Project Dialog feature. We'll review this list at our next meeting.

## Tasks

1. Open new project UI and viewmodel — ✅ **completed**
   - Read `Views/NewProjectPromp.xaml` and `Views/NewProjectPromp.xaml.cs`
   - Confirmed a matching ViewModel is needed and created a skeleton

2. Design XAML for dialog — ✅ **completed**
   - Added framework selection (JSON/INI/PO/YAML)
   - Added project folder browser
   - Simplified UI to show language list instead of complex package management
   - Files are now automatically created based on selected framework

3. Wire-up code-behind and viewmodel — ✅ **completed**
   - `ViewModels/NewProjectViewModel.cs` added with IProjectService integration
   - `TranslationPackage` and `TranslationEntry` types added (still used elsewhere)
   - Browsing for folders wired via `DialogService`
   - Validation implemented and Create button is enabled/disabled based on validity

4. Add multi-framework initial file support — ✅ **completed**
   - Implemented save strategies for PO, INI, and YAML formats
   - Added `PoSaveStrategy.cs`, `IniSaveStrategy.cs`, `YamlSaveStrategy.cs` to `Toucan.Core/Services/SaveStrategies/`
   - Modified `ProjectService.CreateLanguage` to accept `SaveStyles` parameter
   - Added `ProjectService.CreateProject` method for complete project initialization
   - All strategies registered in `App.xaml.cs`

5. Move `LanguagePrompt` invocation to view — ⏸️ **deferred**
   - Current implementation uses LanguagePrompt from ViewModel for consistency
   - Can be refactored later for stricter MVVM if needed

6. Improve validation & UI polish — ✅ **completed**
   - Simplified UI by removing manual file path entry (auto-generated now)
   - Basic validation for project name and folder
   - Create button validation
   - Clean, minimal interface showing framework, folder, and languages

7. Add unit & integration tests — ⏸️ **not-started**
   - Unit tests for `NewProjectViewModel` validation
   - Integration tests for multi-framework file generation
   - Recommended for future work

## Implementation Summary

### What Was Completed

- **Multi-framework support**: The dialog now supports creating projects in JSON, INI, PO, and YAML formats
- **Save strategies**: Implemented pluggable save strategies for all supported formats
- **Simplified UX**: Removed complex package/file browsing UI in favor of automatic file creation
- **Project creation flow**: Complete end-to-end flow from UI to actual file creation on disk
- **Validation**: Basic validation ensures required fields are set before creation

### Technical Changes

1. **Toucan.Core**:
   - Added `PoSaveStrategy`, `IniSaveStrategy`, `YamlSaveStrategy`
   - Updated `IProjectService` interface with new methods
   - Modified `ProjectService.CreateLanguage` to accept `SaveStyles`
   - Added `ProjectService.CreateProject` method

2. **Toucan (WPF)**:
   - Updated `NewProjectViewModel` to use `IProjectService`
   - Simplified XAML to remove package management
   - Wired up project creation in dialog confirmation
   - Updated `StartScreenViewModel` and `MainWindowViewModel` to pass `IProjectService`
   - Registered all save strategies in `App.xaml.cs`

### Next Steps

- Test the implementation with all supported frameworks (JSON, INI, PO, YAML)
- Consider adding unit tests for the save strategies
- Optionally add a project template system for more advanced scenarios
- Consider adding validation for duplicate language codes

---

Implementation is ready for testing and use!
