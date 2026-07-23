# Virtualized source visualisation plan

Reviewed on 2026-07-23 for the `visualisation` branch.

## Goal

Replace the full-document Razor/HTML source view with a read-only, virtualized editor that remains responsive for large decompiled types while preserving DnSpyXDX navigation, search, themes, token focus, block structure, accessibility, and offline operation.

This milestone covers source presentation. It does not add IL modes, exact semantic-span production, editing, language servers, or a persistent disk cache.

## Decision

Use **Monaco Editor**, loaded from a locally bundled ESM build.

Monaco is the preferred fit because it provides virtualized line rendering, explicit text models, built-in find, read-only and accessibility modes, large-file optimizations, model decorations, bracket matching, view-state save/restore, and range reveal. Models have stable URIs and explicit disposal, which maps cleanly to document tabs. Its API also provides the semantic-token, hover, and definition-provider extension points needed later.

CodeMirror 6 is a credible smaller fallback and also draws only viewport content, but adopting it would require more custom work to reproduce Monaco's IDE-like interaction and future semantic features. Reconsider CodeMirror only if the Photino worker/bundle spike fails or Monaco's packaged size and startup cost exceed the agreed budgets.

Do not use Monaco's AMD build. AMD support is deprecated; use ESM and a bundler. Do not load editor files, fonts, or workers from a CDN because the application must remain offline-capable.

## Current bottlenecks

`SourceView.razor` currently:

1. Runs `CodeHighlighter.Highlight` across the complete source string on every document change.
2. Creates an HTML span for each classified token.
3. Inserts the complete highlighted document into one `<pre>`.
4. Queries the full DOM for link highlighting, token focus, find results, brace pairs, and block guides.
5. Measures every brace pair to draw a document-sized SVG overlay.

This makes CPU time, allocations, DOM size, layout work, and disposal latency scale with the entire document. Razor component virtualization would not fix these costs because the expensive unit is a single generated HTML document.

## Architecture

### Ownership boundary

Blazor remains responsible for:

- tabs, active-document selection, history, and cancellation
- document identity, source text, reference metadata, and diagnostics
- application theme selection
- navigation requests back into the backend

A small JavaScript module owns:

- the single editor instance for the active document area
- Monaco models and model-scoped decorations
- per-document Monaco view state
- editor mouse, keyboard, find, focus, and resize events
- Monaco resource disposal

Keep Monaco behind this interop surface:

```text
initialize(container, dotNet, options)
setDocument(documentKey, language, text, references, focusOffset)
closeDocument(documentKey)
setTheme(themeId)
focus()
openFind()
captureViewState(documentKey)
dispose()
```

Do not expose Monaco objects to .NET or spread Monaco calls through unrelated JavaScript.

### Document identity

Give every model a deterministic, collision-resistant URI derived from:

```text
module MVID + metadata token + language + settings fingerprint
```

The first implementation may use C# and the current settings only, but the key must leave room for language modes. Never reuse one URI for different content. Dispose its model, decorations, listeners, and saved view state when the tab closes or its assembly unloads.

### Models and view state

- Create one editor with `model: null` and switch explicit models into it.
- Before switching, call `saveViewState()` for the outgoing document.
- Attach the target model, then restore its saved state.
- A new document starts at the top unless it has a focus target.
- For search/source member navigation, convert the member's source offset to a Monaco position and reveal it near the top.
- Define whether navigation history snapshots view state immediately or only when leaving a document; use one policy consistently.

Do not keep models for every document forever. The UI model cache must follow the bounded cache policy and may recreate an evicted model from cached source text.

### Text and offset contract

Keep `DecompilerDocument.Text` as plain text. Do not send highlighted HTML.

All .NET offsets are UTF-16 code-unit offsets, matching JavaScript strings and Monaco model offsets. Document and test this rule before semantic decorations are introduced. Normalize line endings once in the backend or presentation adapter; reference offsets must be computed after the same normalization.

Add presentation records with explicit spans rather than name maps:

```csharp
public sealed record SourceDecoration(
    int StartOffset,
    int Length,
    string Classification,
    SymbolId? Target,
    string? Tooltip);
```

The initial migration can use Monaco's C# tokenizer for lexical colors and translate current token comments/focus metadata into decorations. Exact navigation continues to be a later backend milestone; do not encode the existing name heuristic into the new editor contract.

### Tokenization and decorations

- Use Monaco's bundled C# Monarch tokenizer for the first version.
- Map DnSpyXDX themes through `defineTheme` and `setTheme`.
- Use a decorations collection for clickable references, focused declarations, diagnostics, metadata-token markers, and any classifications Monaco cannot express.
- Use Monaco's native bracket-pair colorization and guides instead of the current full-document brace scan and SVG overlay.
- Use Monaco's built-in find widget instead of maintaining a second source-find implementation.
- Disable editing affordances, suggestions, drag/drop, validation, and language services that have no read-only value.
- Keep `largeFileOptimizations` enabled and choose an explicit maximum tokenized line length.

Incremental tokenization should be owned by Monaco and its worker where supported. Do not pre-tokenize the complete document in .NET and do not create one decoration per lexical token.

### Navigation behavior

Preserve these interactions:

- Click a resolvable reference to navigate in the current tab.
- Ctrl+click opens in a new tab.
- Alt/Shift preserve text-selection behavior.
- Hover highlights occurrences only when the cost is bounded to Monaco decorations/providers.
- Opening a member from search or source reveals and briefly emphasizes its declaration in the declaring-type document.
- Back/forward restores the prior document and view state.

Implement navigation using editor mouse events plus offset-to-span lookup. Keep spans sorted and use binary search; avoid scanning every reference on pointer movement.

## Packaging and host integration

### Asset pipeline

1. Add a small frontend package beneath `src/DnSpyXDX.UI` with an exact `monaco-editor` version and committed lockfile.
2. Bundle the ESM editor entry, C# language contribution, CSS, fonts, and editor worker into deterministic files under `wwwroot`.
3. Commit generated bundles so ordinary `dotnet build` and offline application startup continue to require only the documented .NET SDK and native prerequisites.
4. Provide one explicit asset-regeneration command for maintainers with a pinned Node version.
5. Fail CI when regenerated assets differ from committed assets.
6. Add Monaco's MIT license and third-party notices to release license output.

Avoid importing all language contributions and language-service workers. Bundle only editor core, C#, and the base editor worker unless measurements show another module is required.

### Photino spike

Before the full migration, build a minimal editor page and verify on both Windows WebView2 and Linux WebKitGTK:

- ESM bundle loading from the application's actual origin
- editor worker creation without network access
- worker and font URLs resolve from published output
- clipboard, Ctrl+F, Ctrl+click, selection, and context menu behavior
- `ResizeObserver`/automatic layout during pane and window resize
- theme application before the editor becomes visible
- clean shutdown without worker or JS-disconnection errors

Monaco workers cannot be assumed to behave like normal scripts, and `file://` pages cannot create them. Treat successful worker startup on both Photino backends as the milestone gate. If Photino's origin cannot support workers, investigate an application-local HTTP origin before accepting main-thread fallback.

## Implementation phases

### 1. Integration spike

- Bundle a pinned Monaco ESM build locally.
- Render a read-only C# model in Photino.
- Prove the worker and all assets load offline on Windows and Linux.
- Measure bundle size, editor startup, a 5 MB document, and disposal.
- Record the choice or fallback in an architecture decision.

Exit: both platforms pass the spike and the editor does not fetch remote resources.

### 2. Editor adapter

- Add `monaco-editor.js` with the narrow interop API above.
- Replace `<pre>` output in `SourceView.razor` with an editor host element.
- Configure read-only behavior, accessibility, resizing, C# tokenization, and find.
- Dispose the editor, listeners, and worker-facing resources safely.

Exit: normal documents retain copy, selection, find, scrolling, and keyboard accessibility.

### 3. Tabs and view state

- Create stable model keys.
- Save and restore cursor, selection, folding, and scroll state per tab.
- Dispose models on tab close and assembly unload.
- Preserve state across back/forward navigation.
- Decide which subset of view state belongs in persisted sessions.

Exit: switching among tabs neither resets view state nor leaks models.

### 4. DnSpyXDX presentation features

- Port all application themes to Monaco themes.
- Replace custom brace spans/guides with Monaco bracket features.
- Add declaration focus/reveal and metadata-token decorations.
- Add reference hover/click/Ctrl+click behavior using the current available navigation data.
- Remove superseded highlighting, source-find, and block-structure code.

Exit: the old source view can be deleted without losing supported behavior.

### 5. Large-document and cache controls

- Add a bounded LRU for UI models and presentation metadata.
- Set separate limits for model count and approximate text/decorations bytes.
- Cancel pending document/presentation work on close or replacement.
- Avoid re-sending unchanged text across JS interop.
- Instrument creation, model switch, first render, eviction, and disposal timings.

Exit: large-document actions satisfy the agreed performance and memory budgets.

### 6. Verification and rollout

- Run unit, interop, publishing, and manual GUI tests.
- Test both WebView engines at multiple application zoom levels and themes.
- Remove dead CSS/JavaScript and `CodeHighlighter` only after equivalent coverage exists.
- Update the implementation roadmap and reliability documentation.

Exit: the new editor is the only production source path and both platform smoke tests pass.

## Test strategy

### Unit tests

- document-key stability and uniqueness
- UTF-16 offset-to-position conversion, including CRLF and surrogate pairs
- decoration bounds, sorting, overlap policy, and invalid-span rejection
- reference hit lookup
- cache recency, byte accounting, eviction, and disposal callbacks
- theme-to-Monaco token/color mapping

### JavaScript tests

- idempotent initialization and disposal
- model creation, switching, eviction, and URI reuse prevention
- view-state save/restore
- click versus Ctrl/Alt/Shift behavior
- focus range reveal
- theme switching without recreating models
- no update when document key and content version are unchanged

### Integration and GUI tests

- publish and launch on `win-x64` and `linux-x64`
- open, decompile, switch tabs, find, navigate, change theme, close, and restore
- confirm all resources load offline and no external requests occur
- confirm editor worker creation and absence of console errors
- exercise 1 MB, 5 MB, and 25 MB generated C# fixtures plus pathological long lines
- repeatedly open/close large documents and verify memory returns near its steady-state bound
- use keyboard-only navigation and screen-reader mode on at least one platform

### Initial performance budgets

Record hardware and WebView versions with results. Refine these budgets after the spike, but do not remove them without replacements:

| Scenario | Initial budget |
| --- | ---: |
| Switch to an already-created model | 200 ms to usable input |
| Close a large active document | 100 ms UI-thread response |
| Open a 5 MB cached source document | 2 s to usable input |
| Scroll a 5 MB document | No repeated UI-thread tasks over 100 ms |
| Repeat open/close cycle | Memory plateaus within configured cache bounds |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Worker URLs fail in one Photino backend | Make the two-platform worker spike the first gate; bundle worker URLs explicitly |
| Bundle adds excessive size/startup cost | Tree-shake to editor core, C#, and one worker; measure published output before committing |
| Monaco and existing Blazor shortcuts conflict | Define shortcut ownership and test Ctrl+F, history, zoom, and navigation centrally |
| Models survive closed tabs | Centralize model ownership and assert disposal in JS tests and diagnostics |
| Decorations recreate full-document work | Use lexical tokenization for colors and reserve decorations for semantic/application data |
| Offset drift breaks navigation | Establish UTF-16 and newline contracts with boundary tests |
| Theme appearance regresses | Create an explicit Monaco theme mapping and screenshot/manual comparisons for all themes |
| Accessibility regresses | Keep accessibility detection on, provide an ARIA label, and test keyboard/screen-reader behavior |
| Generated assets become stale | Pin dependencies, commit the lockfile and bundles, and verify regeneration in CI |

## Open decisions for the spike

- Which bundler produces the smallest maintainable deterministic ESM/worker output?
- Does the current Photino asset origin support module workers on both platforms?
- Should generated frontend assets remain committed, or should release builds provision Node in CI while local .NET builds consume the last generated bundle?
- Which Monaco view-state fields should persist across application restarts rather than only tab switches?
- What cache byte limit is appropriate after measuring representative assemblies?
- Are built-in C# tokens sufficient to match the existing classification palette, or are a small number of semantic decorations required?

## Primary references

- [Monaco Editor repository and integration guidance](https://github.com/microsoft/monaco-editor)
- [Monaco editor construction options](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.IStandaloneEditorConstructionOptions.html)
- [Monaco code-editor API](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.ICodeEditor.html)
- [Monaco API index](https://microsoft.github.io/monaco-editor/typedoc/index.html)
- [Monarch tokenizer documentation](https://microsoft.github.io/monaco-editor/monarch-static.html)
- [CodeMirror system guide](https://codemirror.net/docs/guide/)
- [Photino.Blazor architecture](https://docs.tryphotino.io/Photino-Blazor)

