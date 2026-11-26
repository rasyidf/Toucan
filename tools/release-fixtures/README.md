# Release fixtures

Writes the projects and settings in `tests/fixtures/releases/<version>/` the way one released Toucan saves them, so
`ReleaseMigrationTests` can check that 1.x opens them with what that release saw. It is not built with the solution.

For each release tag:

1. Check the tag out in its own worktree: `git worktree add --detach ../toucan-v0.26.0 v0.26.0`.
2. Make a console project next to `Program.cs` that references that worktree's `Toucan.Core/Toucan.Core.csproj` and
   `Toucan.Modules.Defaults/Toucan.Modules.Defaults.csproj`, plus the packages `Microsoft.Extensions.Logging` and `NSubstitute`,
   targeting the SDK the release used.
3. Run it with `HOME` pointed at an empty scratch folder, so the release's recovery drafts and recent projects do not touch your
   own Toucan folder: `HOME=/tmp/toucan-home dotnet run -- <output folder> 0.26.0`.
4. Copy `<output folder>` to `tests/fixtures/releases/0.26.0/`.

Each project gets one approved translation (fr `greeting`), one comment (fr `farewell`) and one edit (en `farewell`), then
is saved through the release's own `ProjectLifecycleService`. `expected/<project>.json` is what the same release reads when it
opens the saved project again; quirks of that release (sidecars listed as languages, duplicated keys) are kept, and the test
filters or explains them.
