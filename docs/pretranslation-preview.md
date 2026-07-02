# Pretranslation — Preview & Progress

Toucan's pretranslation engine now supports safe preview runs with progress reporting and an explicit commit step.

Key behaviors:
- PreviewOnly mode: Providers can be asked to perform a dry-run (PreviewOnly) — the result is returned as a list of proposed translations but the project files are not modified.
- Progress reporting: Pretranslation providers implement progress/reporting via IProgress&lt;PretranslationProgress&gt; so the UI can show completed/total counts and a progress-bar.
- Cancellation: Long-running preview runs or provider calls can be cancelled via a CancellationToken.
- Commit step: After previewing translations, the UI shows a summarized list and lets the user Apply Preview (commit) to update the local translations.

UI integration:
- The Pre-Translate window displays a progress bar while preview runs are in-flight and a list of proposed translations (namespace, language, source, proposed translation) once complete.
- An "Apply Preview" button lets the user apply those results to the in-memory translations; the user must explicitly click Apply for changes to be persisted.

Privacy note:
- Preview mode sends your source strings to the selected provider in order to obtain translations. Always be mindful of provider privacy policies when translating sensitive strings.
