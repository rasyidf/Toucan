---
title: "M7 spelling engine decision"
status: active
updated: 2026-10-23
summary: "Pin WeCantSpell.Hunspell 7.0.1 for local spelling under its MPL 1.1 option, ship exact source/notices, and keep dictionary grants separate. Packaging verification is pending."
---

# M7 spelling engine decision

Toucan selects unmodified **WeCantSpell.Hunspell 7.0.1**, upstream commit `ab5709d95b2d23541984d22baa0ab2d1e783582f`, using its **MPL 1.1** alternative. This selection applies to the engine alone. No dictionary is bundled or offered for automatic download. User imports remain local and are not approved for redistribution by importing them.

| Candidate | Evidence and decision |
|---|---|
| WeCantSpell.Hunspell 7.0.1 | Pinned NuGet metadata has a net10.0 assembly with no runtime dependencies. Upstream documents concurrent queries, AFF/DIC loading, cancellation and time-bounded query options. Managed code avoids native per-architecture library shipping. Selected, with a source archive and notices alongside release artifacts. |
| Native Hunspell | Upstream offers MPL/GPL/LGPL alternatives. Requires native bindings and binaries for each claimed OS/architecture, adding deployment and ABI verification. Not selected for this delivery. |
| A simple word-set checker | Easy to deploy but lacks Hunspell affix/compound semantics and existing dictionary compatibility. Not sufficient for the requested layer. |

Sources inspected: [exact release source](https://github.com/aarondandy/WeCantSpell.Hunspell/tree/7.0.1), [exact engine license](https://github.com/aarondandy/WeCantSpell.Hunspell/blob/7.0.1/license.txt), [NuGet 7.0.1](https://www.nuget.org/packages/WeCantSpell.Hunspell/7.0.1), [native Hunspell license](https://github.com/hunspell/hunspell/blob/master/license.hunspell) and the [official MPL 1.1 text](https://www.mozilla.org/MPL/1.1/).

`third-party/WeCantSpell.Hunspell/7.0.1/provenance.json` records package/source hashes, chosen grant, upstream commit and net10.0 DLL hash. `source.zip` contains the unmodified library sources, project file, license and build-context text from that commit. Upstream test dictionaries, benchmarks and icon assets are excluded. The complete original upstream archive and downloaded package are inspection artifacts under `build/`, not product assets. Source distribution must accompany the notices in every supported publishing mode; release verification checks this requirement. No claim for trimming/AOT or a different runtime target is implied by the net10.0 record.

The adapter uses engine cancellation/query limits, bounded local inputs, and rejects parse warnings rather than silently accepting a partially loaded dictionary. Dictionary locale selection, word lists and UI operations belong to Toucan. License/provenance checks must be repeated for a version change; a UI/plugin boundary is not evidence that an unreviewed artifact can ship. Runtime and packaged-platform verification remain tracked in the delivery plan.
