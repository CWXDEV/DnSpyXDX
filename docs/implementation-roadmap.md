# Implementation roadmap

## Phased implementation plan

The estimates below assume one experienced C# developer working mostly full-time. They are planning ranges, not commitments.

| Phase | Deliverable | Estimate | Exit condition |
| --- | --- | ---: | --- |
| 0. Feasibility spike | Photino + Blazor + .NET 10 shell; ILSpy package loads a test DLL on both OSes | 2–4 days | Same branch launches and decompiles one type on Windows and Linux |
| 1. Application skeleton | Project split, DI, logging, settings, native dialogs, CI builds | 3–5 days | Clean publish artifacts for both RIDs |
| 2. Assembly workspace | Open/close sessions, lazy virtualized tree, metadata/error views | 1–2 weeks | Large test assembly is browsable without UI stalls |
| 3. Decompiled documents | Monaco, type/member decompilation, tabs, caching, cancellation | 1–2 weeks | Stable type/member viewing and tab restoration |
| 4. Navigation and search | Symbol IDs, history, semantic spans, Ctrl+click, indexed name search | 1–2 weeks | Navigation acceptance tests pass |
| 5. Project and `.slnx` export | Whole-project adapter, staging, multi-project mapping, reports, optional build | 1–2 weeks | One- and multi-assembly exports are deterministic and validated |
| 6. Hardening | Malformed inputs, resource/path safety, memory/concurrency controls, recovery | 1–2 weeks | Adversarial fixture suite cannot corrupt output or freeze UI indefinitely |
| 7. Release engineering | installers/archive layout, prerequisites, licenses, smoke tests, docs | 3–5 days | Reproducible Windows/Linux x64 release candidate |

A realistic read-only MVP is approximately **6–10 weeks** for one developer. Semantic source hyperlinks and reliable multi-project export are the two areas most likely to move the schedule.

## First implementation backlog

Build these tickets in order:

1. Create the .NET 10 solution and Photino.Blazor host.
2. Add a Razor three-pane shell: assembly tree, source tabs, status/output panel.
3. Add `IDecompilerBackend` and a test fake.
4. Implement `AssemblySession` with `PEFile` and `UniversalAssemblyResolver`.
5. Show assembly metadata, references, namespaces, and types lazily.
6. Decompile a selected type into a plain read-only source view.
7. Integrate Monaco and preserve model/view state per tab.
8. Add member nodes and member-level decompilation.
9. Add cancellation, progress, error documents, and an LRU cache.
10. Implement history and symbol identity.
11. Add semantic reference spans and editor navigation.
12. Add workspace-wide name search.
13. Export one assembly with `WholeProjectDecompiler`.
14. Add `SlnxWriter`, multi-project export, and project-reference mapping.
15. Add optional `dotnet build` validation and a persistent export report.
16. Add Windows/Linux publishing and GUI smoke tests.
