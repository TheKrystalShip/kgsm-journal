# TheKrystalShip.KGSM.Journal

The event journal of the KGSM ecosystem as one .NET package.

- **Writing**: the envelope, `EventJournalWriter` (one line per event, appended with `O_APPEND`), the severity and
  outcome scale, and `LeafLifecycle` for a component reporting on itself.
- **Reading**: `EventJournalReader` and `FederatedEventSource` tail one journal or every producer's, with cursors
  (`IEventCursorStore`, `FileEventCursorStore`) and gap reporting; `EventJournalHistory` reads back over what a
  journal already holds; `EventService` dispatches typed payloads to handlers.
- **Vocabulary**: `KgsmEventCatalog` and the payload classes, with each field's sensitivity classified.
- **Conformance**: `JournalConformance` checks what producers wrote against the envelope's contract.

```sh
dotnet add package TheKrystalShip.KGSM.Journal
```

```csharp
services.AddKgsmJournal("my-leaf", typeof(Program).Assembly);          // a producer's writer
services.AddKgsmJournalReader(new JournalReadOptions());               // a reader of the engine's journal
services.AddKgsmJournalFederation(cursorPath: "/var/lib/my-leaf/cursors.json");   // every producer's
```

Reflection-free and Native-AOT-safe; it depends on the logging and registration abstractions only.
Rules for working in the package: `CLAUDE.md`.
