# Assembly workspace and navigation

## Opening and indexing an assembly

An `AssemblySession` should own all disposable metadata/decompiler state for one opened file:

1. Normalize and validate the path.
2. Open it with `PEFile`, never reflection.
3. Confirm it contains CLI metadata.
4. Read the module MVID, assembly name, target framework, architecture, entry point, references, and resources.
5. Create a `UniversalAssemblyResolver`.
6. Add probe paths in this order:
   - the opened assembly's directory
   - user-configured reference directories
   - directories of other assemblies opened in the workspace
   - installed .NET reference/shared-framework locations when appropriate
7. Build only the top-level tree immediately.
8. Lazily enumerate namespaces, types, and members as tree nodes expand.

Illustrative setup:

```csharp
var peFile = new PEFile(path, PEStreamOptions.PrefetchEntireImage);

var resolver = new UniversalAssemblyResolver(
    path,
    throwOnError: false,
    peFile.DetectTargetFrameworkId());

resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);

var settings = new DecompilerSettings
{
    ThrowOnAssemblyResolveErrors = false
};

var decompiler = new CSharpDecompiler(peFile, resolver, settings);
```

Treat this as a shape, not copy-paste-complete production code: the exact constructor overloads should be locked to the chosen package version and covered by an integration test.

### Threading rule

`CSharpDecompiler` instances are not thread-safe. Do one of the following:

- serialize decompilation work per assembly session; or
- create a new decompiler instance for each concurrent request while sharing immutable configuration.

Start with one queue per session. It is simpler, bounds memory, and makes cancellation predictable. Allow separate assemblies to decompile concurrently up to a global limit such as `max(1, ProcessorCount - 1)`.

### Caching

Cache decompiled documents by:

```text
(file content fingerprint, metadata token, settings fingerprint, decompiler version)
```

Use a small in-memory LRU cache first. A disk cache can be added later. Clear session caches when the source file's length or last-write time changes; use a full hash before persisting results across application launches.

## Decompilation and source navigation

### First implementation

Use the current stable `ICSharpCode.Decompiler` package and its `CSharpDecompiler` APIs:

- `DecompileTypeAsString(FullTypeName)` for a complete type document.
- `DecompileAsString(EntityHandle[])` for selected members.
- `DecompileWholeModuleAsString()` only for explicit whole-module operations; it can be expensive.

For the first vertical slice, display plain C# in a read-only Monaco model and navigate only from the tree and history.

### Clickable references

Do not try to recover references by regex-parsing the final C# text. That fails for overloads, aliases, generic types, operators, and ambiguous names.

Instead:

1. Ask the decompiler for a syntax tree for the selected definition.
2. Format it through a token writer/output visitor that records the character range of each identifier.
3. Read the semantic annotation or resolved entity associated with each relevant syntax node.
4. Convert the target entity to your own `SymbolId` plus assembly identity.
5. Return plain text and `ReferenceSpan` records to the UI.
6. Add Monaco decorations for those spans and handle Ctrl+click/double-click in JavaScript.

Use a data shape such as:

```csharp
public sealed record ReferenceSpan(
    int StartOffset,
    int Length,
    SymbolId? LocalTarget,
    AssemblyReferenceId? ExternalAssembly,
    string Tooltip);
```

If a reference points into an unopened dependency, show the target assembly name and offer to locate/open it. Never silently resolve to a same-named symbol in another module.

### Monaco integration

Keep Monaco behind one small JavaScript module:

- `createEditor(element, options)`
- `setDocument(id, text, spans)`
- `revealRange(start, length)`
- `setTheme(theme)`
- `disposeDocument(id)`

The Blazor side owns tabs and navigation state. JavaScript owns only editor models, view state, and mouse/keyboard events. Avoid rendering large source files as thousands of Razor DOM nodes.

## Tree, tabs, and navigation state

### Tree

Use lazy loading and virtualization. A large Unity or application assembly can have tens of thousands of members; eagerly rendering the entire tree will make a WebView UI feel broken.

Recommended nodes:

```text
Assembly
├── References
├── Resources
└── Namespaces
    └── Namespace
        └── Type
            ├── Base types
            ├── Fields
            ├── Properties
            ├── Events
            ├── Constructors
            ├── Methods
            └── Nested types
```

Sort types and members deterministically. Preserve metadata order as an optional setting for reverse-engineering work.

### Tabs and history

Separate the selected tree node from the active document. A document tab should contain:

- document key and symbol ID
- title and assembly name
- decompiler settings fingerprint
- Monaco view state (cursor, selection, scroll position)
- loading/error/ready state

Navigation history entries should store the symbol, tab preference, caret range, and scroll position. Back and forward should restore view state without forcing a re-decompile when the cache is valid.
