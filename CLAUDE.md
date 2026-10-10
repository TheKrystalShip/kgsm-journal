# KGSM-Lib Development Guide

## Project Overview

KGSM-Lib is the .NET library through which every other C# project reaches the engine, the watchdog and
firewall sockets and the event journals. Linux-only; KGSM itself is not bundled. Role, contracts and peers:
`../kgsm-docs/components/kgsm-lib.md`; model: `../kgsm-docs/systems/engine/kgsm-lib.md`.

Entry point: **`IKgsmClient`**, the facade every consumer starts from, over
BlueprintService/InstanceService/EventService, over ProcessRunner/IEventSource.

**Each area's rules live in a `CLAUDE.md` beside its code**: `kgsm-lib/Json/` (source-generated
deserialization — how to register a type), `kgsm-lib/Services/` (process execution, the event system,
cursors and positions, reading history), `kgsm-lib/Events/` (`KgsmEventCatalog`),
`kgsm-lib/Core/Scheduling/` (the maintenance grammar), `journal/` (the writer, conformance,
`LeafLifecycle`) and `kgsm-lib.Tests/`.

## Library-wide rules

- **Reflection-free / Native-AOT-safe.** The library is `IsAotCompatible`; every deserialized type is
  registered in `KgsmJsonContext` or it throws at runtime (`kgsm-lib/Json/CLAUDE.md`). This is what lets
  an AOT consumer embed it.
- **DI via `ServiceCollectionExtensions`:**

  ```csharp
  services.AddKgsmServices("/path/to/kgsm.sh");        // journal at its default location
  services.AddKgsmServices(new KgsmOptions { ... });   // full control
  ```

  Lifetimes: `IProcessRunner` transient (stateless executor); `IEventSource`, `IEventCursorStore`,
  `IEventService`, `IKgsmClient` singleton (one transport per process); `IBlueprintService`,
  `IInstanceService` transient (delegate to ProcessRunner).
- **Always use `ConfigureAwait(false)` in library code** to avoid deadlocks in consumers.
- **Services managing unmanaged resources** (sockets, event handlers) implement the dual
  `Dispose()`/`Dispose(bool)` pattern with a `_disposed` flag, and check `_disposed` before operating.
- **All public APIs carry XML doc comments** (`<summary>`, `<param>`, `<returns>`, `<exception>`);
  `<DocumentationFile>` produces the XML for IntelliSense/NuGet.

## Building and packaging

```bash
dotnet build kgsm-lib.sln                    # Debug build
dotnet build -c Release kgsm-lib.sln         # Release (generates the NuGet package)
dotnet test  kgsm-lib.sln
```

`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` generates the package on Release builds.
**Because of that flag, `dotnet pack` does not reliably build first** — it packs whatever is already in
`bin/Release/`, so straight after an edit it will happily produce a package containing the *previous*
build, and consumers restore code you did not write. Build first, then publish:

```bash
dotnet build kgsm-lib/kgsm-lib.csproj -c Release        # this is what makes the .nupkg
../scripts/publish-packages.sh kgsm-lib                 # → the org's GitHub Packages feed
```

Verify before trusting it — a stale package fails as a baffling "my change isn't there":
`unzip -p <nupkg> lib/net10.0/TheKrystalShip.KGSM.dll | strings -el | grep '<a new string literal>'`
(`-el` matters: .NET string literals are UTF-16, so plain `strings` finds type names but never message
text).

**Package ID**: `TheKrystalShip.KGSM.Lib` · **Namespace**: `TheKrystalShip.KGSM`

### Shipping it (there is no `deploy/` here)

This is a library, not a service: it has no install prefix, no systemd unit, and no
`deploy/setup.sh` + `deploy/deploy.sh` pair. Shipping a change means bumping `<Version>`, publishing,
then bumping the matching `<PackageReference>` pin in each consuming repo. **A published version can
never be replaced** — pushing one again is a `409`, and NuGet caches by `id+version`, so every change
consumers must see needs a version bump. While iterating, bump to a prerelease (`4.26.0-dev.1`,
`-dev.2`, …) and push each one: a push is fetchable within seconds, so the loop costs a version number
rather than time.

## Version tracking

- **Version source:** `<Version>` in `kgsm-lib/kgsm-lib.csproj`.
- Bump the version whenever you make a user-facing change (patch for fixes, minor for features, major
  for breaking changes), with a `CHANGELOG.md` entry under `## [Unreleased]`.

## Read before changing

- `../kgsm-docs/reference/event-envelope.md`, `event-journal.md`, `event-vocabulary.md`: the journal line,
  the segment rules and the event catalog this library mirrors.
- `../kgsm-docs/reference/maintenance-windows.md`: the grammar `Core/Scheduling/` implements.
- `../kgsm-docs/reference/action-manifest.md`: the manifest `KgsmActions` mirrors.
- `../kgsm-docs/reference/firewall-authority.md`: the firewall client's wire contract.
- `../kgsm-docs/decisions/0017-lib-is-the-only-csharp-door.md`.
