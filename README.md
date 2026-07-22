# DnSpyXDX

<p align="center">
  <img src="src/DecompilerApp.UI/wwwroot/images/dnspyxdx-logo.webp" width="88" height="88" alt="DnSpyXDX logo">
</p>

<p align="center">
  <img src="src/DecompilerApp.UI/wwwroot/images/xdding.webp" width="400" alt="DnSpyXDX animation">
</p>

> A fast, cross-platform desktop explorer for inspecting, searching, decompiling, and exporting managed .NET assemblies—without loading or executing them.

DnSpyXDX is a focused, read-only assembly browser for Windows and Linux. It combines a native Photino window, a Blazor interface, and the ILSpy decompiler engine to provide a compact desktop workflow for understanding compiled C# applications and libraries.

## Highlights

- Open managed `.dll` and `.exe` files without executing assembly code
- Lazily browse assemblies, references, resources, namespaces, types, and members
- Decompile complete types or individual members into readable C#
- Search types, methods, fields, properties, and events across one or all open assemblies
- Reveal search results in the assembly tree
- Export open assemblies as SDK-style C# projects in a `.slnx` solution
- Restore open assemblies, document tabs, search state, panel layout, and window placement
- Navigate large trees and source documents with persistent, themed scrollbars
- Read semantic source highlighting, metadata tokens, and rainbow brace pairs

## Safety model

DnSpyXDX treats assemblies as untrusted input. It reads PE files and CLI metadata through `ICSharpCode.Decompiler`; it does not inspect files using `Assembly.Load`, reflection, or an `AssemblyLoadContext`.

Malformed files can still consume significant CPU or memory while being parsed or decompiled. Do not treat the current in-process architecture as a security boundary.

## Requirements

- .NET 10 SDK for development
- Windows x64 with WebView2, or
- Linux x64 with GTK 3, WebKitGTK 4.1, and libnotify

Linux package names vary between distributions.

## Run from source

```bash
dotnet restore DnSpyXDX.slnx
dotnet run --project src/DecompilerApp.Host
```

## Build and test

```bash
dotnet build DnSpyXDX.slnx
dotnet test DnSpyXDX.slnx
```

## Publish

```bash
dotnet publish src/DecompilerApp.Host -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false
dotnet publish src/DecompilerApp.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

Trimming, Native AOT, and single-file publishing are intentionally disabled for the initial release.

## Project structure

- `DecompilerApp.Host` — Photino executable, native dialogs, and session/window persistence
- `DecompilerApp.UI` — Blazor desktop interface and source presentation
- `DecompilerApp.Application` — application contracts and workspace state
- `DecompilerApp.Decompilation` — metadata browsing and ILSpy decompilation backend
- `DecompilerApp.Export` — project export, reports, and `.slnx` generation
- `DecompilerApp.Tests` — metadata, decompilation, export, and presentation tests

The internal project names remain deliberately generic so the UI host can be replaced without coupling the architecture to the product name.

## Scope

DnSpyXDX currently focuses on read-only inspection and best-effort source export. Debugging, assembly editing, recompilation, IL patching, and metadata rewriting are outside the current scope.

## Documentation

The original implementation guide is now organized into focused documents:

- [Documentation index](docs/README.md)
- [Product direction and scope](docs/product-direction.md)
- [Application architecture](docs/application-architecture.md)
- [Assembly workspace and navigation](docs/assembly-workspace-and-navigation.md)
- [Project export](docs/project-export.md)
- [Dependencies and platform support](docs/dependencies-and-platform.md)
- [Reliability and testing](docs/reliability-and-testing.md)
- [Implementation roadmap](docs/implementation-roadmap.md)
- [Architecture decisions](docs/architecture-decisions.md)
- [Primary references](docs/references.md)

## License

DnSpyXDX is licensed under the [MIT License](LICENSE). Third-party packages and native components retain their respective licenses.
