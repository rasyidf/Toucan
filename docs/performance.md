---
title: "Performance"
status: active
updated: 2026-11-26
summary: "Measured editor performance at 1,000 to 10,000 keys in 10 languages on a MacBook Air M1, against the v1.0 budgets, and how to repeat the profile."
---
# Performance

## Supported scale for 1.0

Toucan 1.0 supports projects of up to **20,000 translations**, for example 2,000 keys in 10 languages (decided 2026-11-26). Within that scale, on a MacBook Air M1:

| Budget | Limit | Measured at 2,000 keys × 10 |
|---|---|---|
| Usable after opening | 5 s | 3.7 s |
| Warm key search | 250 ms | 229 ms |
| Warm text search | 500 ms | 451 ms |
| Longest UI stall while saving | 100 ms | 36–55 ms |

100 ms is the usual limit for a response that feels immediate; repeated runs at this size stalled for 36 to 55 ms. The record for this machine is in `docs/evidence/`.

Larger projects open with a notice and keep working, more slowly: 4,000 keys × 10 open in 6.3 s. At 100,000 translations saving every file ran out of memory on an 8 GB machine, so save often. Raising the supported scale is 1.x work (below).

The roadmap's original budgets were 5 s, 200 ms and responsive typing for 10,000 keys × 10 languages; they are not met at that size.

## Measurements

MacBook Air M1 (`MacBookAir10,1`, 8 cores, 8 GB), macOS 27.2.1, .NET 10, Release build, 2026-11-26. An i18next project, one file per language; the editor runs in a headless 1280 × 800 window, so rendering is laid out but not drawn. Times include laying out the first page.

| Keys × 10 languages | First open | Warm open | Key search | Text search | Edit | Save 1 file | Save 10 files | Longest UI stall while saving | Managed memory |
|---|---|---|---|---|---|---|---|---|---|
| 1,000 | 3.0 s | 1.5 s | 156 ms | 358 ms | 23 ms | 1.4 s | 1.7 s | 22 ms | 136 MB |
| 2,000 | 3.7 s | 2.0 s | 229 ms | 451 ms | 25 ms | 2.1 s | 3.4 s | 36 ms | 185 MB |
| 4,000 | 6.3 s | 4.1 s | 371 ms | 610 ms | 12 ms | 3.6 s | 5.8 s | 53 ms | 284 MB |
| 10,000 | 10.6 s | 10.1 s | about 800 ms | about 800 ms | 2.2 s | 13.5 s | ran out of memory | – | – |

Typing stays responsive while saving at every measured size (the UI thread never waited more than 53 ms). Content-source sync at 100,000 translations is measured separately in `SyncScaleTests`: planning 0.3 s, a push of 1,000 changes 2.1 s.

**Fixed while profiling:** every editor row validated itself, against the whole project, as its card was built. Opening grew with the square of the project size: 4.6 s at 500 keys, 263 s at 4,000, about an hour projected at 10,000. Rows are now validated when their page is shown, from their own card.

**Still over budget at 10,000 keys (1.x):**
- The editor builds a card and rows for every key, not only the page on screen, so opening and searching grow with the whole project.
- Every edit through the workspace copies and re-validates the whole project (about 2 s per edit at 10,000 keys).
- Saving through the desktop takes about three times as long as the save itself, and saving all files at 10,000 keys ran out of memory on this 8 GB machine.

## Repeating the profile

```bash
TOUCAN_PERF=1 TOUCAN_PERF_REPORT=perf.json dotnet test tests/Toucan.Avalonia.Tests -c Release --filter FullyQualifiedName~PerformanceProfileTests
```

`TOUCAN_PERF_KEYS` sets another size; `TOUCAN_PERF_LOG` writes a line as each phase ends, which shows a phase that hangs. Record a run with `tools/record-evidence.sh check performance --result passed|failed --notes "<numbers>"`.
