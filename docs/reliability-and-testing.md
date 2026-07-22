# Reliability and testing

## Reliability and security

Assemblies are untrusted input. Even when no code is executed, malformed or adversarial metadata can cause exceptions, high memory use, deep recursion, or very slow decompilation.

Implement from the start:

- cancellation tokens for open, tree enumeration, decompile, search, and export
- global and per-session concurrency limits
- maximum source/document sizes before showing a confirmation
- structured error results rather than UI-thread exceptions
- logging without dumping proprietary decompiled source by default
- atomic export through a staging directory
- path sanitization for namespaces, types, resources, and obfuscated names
- prevention of `..`, rooted paths, reserved Windows names, and case-collision overwrites
- a crash-recovery/session file that stores paths and UI state, not assembly bytes

For a hardened later release, move decompilation and export into a worker process with a memory limit and restart policy. Define the backend interface now so this can be introduced without changing Razor components.

## Testing strategy

Create small source projects in `tests/TestAssemblies` and compile them during the test build. Cover:

- class library and console executable
- generics, nested types, overloads, async/iterator state machines
- records, required members, primary constructors, extension members supported by the chosen compiler
- enums, attributes, explicit interface implementations, operators, events, and indexers
- embedded resources and satellite assemblies
- missing dependency and mismatched dependency version
- duplicate assembly/type names in separate files
- invalid PE, native PE, truncated DLL, and obfuscated/pathological identifiers
- .NET Framework, .NET Standard, and modern .NET targets

Assertions should focus on semantic behavior, not exact whitespace for every decompiler version. Golden tests are appropriate for the `.slnx`, stable project structure, symbol spans, and path sanitization. Keep a small set of exact decompilation snapshots pinned to the package version to detect unexpected upgrades.

### MVP acceptance test

On both Windows x64 and Linux x64:

1. Launch the published application.
2. Open a known test DLL.
3. Expand a namespace and type.
4. Open two overloaded methods in separate tabs.
5. Ctrl+click a referenced local type and use Back to return.
6. Search for a method and open it.
7. Export the assembly.
8. Confirm a `.csproj`, `.cs` files, `.slnx`, and export report exist.
9. With the .NET 10 SDK installed, validate the solution with `dotnet build`.
