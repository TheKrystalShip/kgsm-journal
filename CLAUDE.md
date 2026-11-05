# CLAUDE.md

## Project Overview

The repository holds `TheKrystalShip.KGSM.Journal`, the one package every producer and reader of the
event journals takes: the envelope and the writer, the severity and outcome scale, the conformance checker,
the federated reader with its cursors and history, `LeafLifecycle`, and the event vocabulary with its
payload catalog. Linux-only. Role, contracts and peers: `../kgsm-docs/components/kgsm-lib.md`.

The engine is not here: its reads are `TheKrystalShip.KGSM.Engine` and its writes the typed invocation in
`TheKrystalShip.KGSM.Engine.Contracts`, both built from the `kgsm` repository.

**Each area's rules live in a `CLAUDE.md` beside its code**: `journal/` (the writer, conformance,
`LeafLifecycle`), `journal/Services/` (the event system, cursors and positions, reading history),
`journal/Events/` (`KgsmEventCatalog`) and `journal.Tests/`.

## Package-wide rules

- **Reflection-free / Native-AOT-safe.** The package is `IsAotCompatible`. Every deserialized type is
  registered in `JournalJsonContext` (`journal/Events/JournalJsonContext.cs`) or it throws at runtime;
  never call a reflection-based `JsonSerializer` overload.
- **Abstractions only.** The package depends on the logging and registration abstractions and the BCL: a
  root-running firewall authority takes it, so no container, host or process model comes with it.
- **DI via `JournalServiceCollectionExtensions`**: `AddKgsmJournal` registers a producer's writer,
  `AddKgsmJournalReader` registers a reader of the engine's journal, `AddKgsmJournalFederation` a reader of
  every producer's. A consumer that keeps its cursor in its own database registers its own
  `IEventCursorStore` after the call.
- **Always use `ConfigureAwait(false)` in library code.**
- **Services holding unmanaged resources** (sockets, handlers) implement the dual `Dispose()`/`Dispose(bool)`
  pattern with a `_disposed` flag and check it before operating.
- **All public APIs carry XML doc comments**; `<DocumentationFile>` produces the XML for IntelliSense.

## Building and packaging

```bash
dotnet build kgsm-lib.sln
dotnet test  kgsm-lib.sln
```

`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` packs on a build. **`dotnet pack` does not reliably
build first**: it packs whatever is in `bin/Release/`, so straight after an edit it produces a package of the
previous build. Build first, then publish:

```bash
dotnet build journal/Journal.csproj -c Release
../scripts/publish-packages.sh kgsm-lib/journal          # the org's GitHub Packages feed
```

Verify a package before trusting it: `unzip -p <nupkg> lib/net10.0/TheKrystalShip.KGSM.Journal.dll | strings -el
| grep '<a new string literal>'` (`-el`: .NET string literals are UTF-16).

### Shipping it

This is a library: no install prefix, no systemd unit, no `deploy/` pair. Shipping a change means bumping
`<Version>`, publishing, then bumping the `<PackageReference>` pin in each consuming repo. **A published
version can never be replaced**: pushing it again is a `409`. While iterating, bump a prerelease (`2.6.0-dev.2`,
...) and push each one. A release is tagged `journal-v<version>`.

## Version tracking

- **Version source:** `<Version>` in `journal/Journal.csproj`.
- Bump it for every user-facing change (patch for fixes, minor for features, major for breaking changes), with
  a `CHANGELOG.md` entry under `## [Unreleased]`.

## Read before changing

- `../kgsm-docs/reference/event-envelope.md`, `event-journal.md`, `event-vocabulary.md`: the journal line, the
  segment rules and the event catalog this package mirrors.
