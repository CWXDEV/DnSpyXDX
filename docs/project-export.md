# Exporting to projects and `.slnx`

`ICSharpCode.Decompiler` already includes `WholeProjectDecompiler`, project information APIs, and an `IProjectFileWriter` extension point. Use these instead of building a C# file splitter from scratch.

### Export pipeline

1. Validate that the destination is empty or create a new child directory. Never partially overwrite an existing project without explicit confirmation.
2. Create a staging directory beside the final destination.
3. For each selected module:
   - make a filesystem-safe, collision-free project directory name;
   - create a resolver with the same probe rules as the viewer;
   - run `WholeProjectDecompiler` with cancellation and progress;
   - emit SDK-style source/resources and a `.csproj`;
   - record warnings for failed members or resources rather than losing the whole report.
4. Map references between selected modules.
5. Where safe, replace a binary `<Reference>` with a `<ProjectReference>` to the corresponding exported project.
6. Emit a root `.slnx`.
7. Move the completed staging directory into place atomically when the filesystem permits it.
8. If a .NET SDK is installed, offer **Validate export** by running `dotnet build` as a separate, cancellable process.
9. Save `export-report.json` or `export-report.md` containing unresolved references, decompilation failures, resource warnings, and build output.

### Minimal `.slnx`

.NET 10 defaults to `.slnx`. A valid simple solution can be written without invoking the CLI:

```xml
<Solution>
  <Project Path="src/LibraryA/LibraryA.csproj" />
  <Project Path="src/LibraryB/LibraryB.csproj" />
</Solution>
```

Use `XmlWriter`, normalized forward slashes, relative paths, deterministic ordering, and no machine-specific absolute paths. You can alternatively invoke `dotnet new sln` and `dotnet sln add`, but direct XML generation allows export on machines that have the self-contained app without the .NET SDK.

### Important expectation

"Export to solution" means reconstructing a best-effort buildable project, not recovering the author's original project. The original package references, conditional MSBuild logic, source generators, analyzers, signing keys, source file boundaries, comments, and exact project settings are not stored completely in the DLL.

The UI should distinguish:

- **Export completed** — files and `.slnx` were written.
- **Validation succeeded** — `dotnet build` returned success.
- **Export completed with warnings** — files exist, but references/resources/members need manual work.
