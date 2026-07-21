# Architecture decisions

## MVP defaults

- **License:** original application code is MIT licensed. Third-party packages retain their own licenses.
- **Source viewer:** the first vertical slice uses a lightweight read-only code surface; Monaco remains the planned editor adapter for semantic decorations and persisted view state.
- **Inputs:** best-effort support for managed PE files, including modern .NET and .NET Framework. Native and invalid PE files are rejected.
- **Export unit:** all currently open assemblies; resolved dependencies are not opened or exported recursively.
- **Validation:** export always writes a report; SDK build validation is optional at the application contract boundary.
- **Isolation:** decompilation is in-process for the MVP behind `IDecompilerBackend`, with serialized work per assembly session.

These choices follow the recommendations in the repository's implementation guide while keeping the host replaceable.
