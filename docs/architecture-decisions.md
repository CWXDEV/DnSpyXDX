# Architecture decisions

## MVP defaults

- **License:** original application code is MIT licensed. Third-party packages retain their own licenses.
- **Source viewer:** the first vertical slice uses a lightweight read-only code surface; Monaco remains the planned editor adapter for semantic decorations and persisted view state.
- **Inputs:** best-effort support for managed PE files, including modern .NET and .NET Framework. Native and invalid PE files are rejected.
- **Export unit:** all currently open assemblies; resolved dependencies are not opened or exported recursively.
- **Validation:** export always writes a report; SDK build validation is optional at the application contract boundary.
- **Isolation:** decompilation is in-process for the MVP behind `IDecompilerBackend`, with serialized work per assembly session.

These choices follow the recommendations in the repository's implementation guide while keeping the host replaceable.

## Decisions to lock before Phase 1

Record these as architecture decision records:

1. **License:** proprietary/permissive/GPL application, and how third-party notices are shipped.
2. **Source viewer:** Monaco versus CodeMirror.
3. **Supported inputs:** modern managed PE only, or best-effort .NET Framework/Unity assemblies too.
4. **Export unit:** one selected assembly, all open assemblies, and/or recursively resolved local dependencies.
5. **Validation:** whether the app merely exports or also promises a build attempt when an SDK exists.
6. **Isolation:** in-process decompilation for MVP versus a worker process from day one.

Recommended initial answers: permissive original code, Monaco, best-effort .NET Framework plus modern .NET, selected/open assemblies only, optional validation, and in-process MVP behind a worker-ready interface.
