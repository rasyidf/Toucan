---
title: Parallel file changes workflow
inclusion: always
---

# Parallel File Changes Workflow

When making changes to multiple files:

1. **Parallel edits first** — Make all independent file changes in parallel (multiple tool calls in one batch). Don't wait for one file edit to complete before starting another if they don't depend on each other.

2. **Integration step after** — Once parallel edits are done, do the integration step (wiring up imports, DI registration, fixing cross-references) as a separate follow-up step.

3. **Build last** — Build only after all changes are complete, not after each individual file.

This applies to: adding new files, modifying ViewModels + XAML + services simultaneously, updating tests alongside implementation.
