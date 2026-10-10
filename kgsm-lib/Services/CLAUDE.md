# Services: process execution, the event system, reading history

## Process execution

All KGSM commands execute via `ProcessRunner.Execute()`; `KgsmResult` wraps `ProcessResult` (Stdout,
Stderr, ExitCode). There is no built-in validation of the KGSM path — ensure `kgsm.sh` exists before
instantiating services.

```csharp
ProcessResult result = _processRunner.Execute(_kgsmPath, "--instances", "--json");
if (result.ExitCode != 0) {
    _logger.LogError("Command failed: {Error}", result.Stderr);
    throw new KgsmException($"Failed: {result.Stderr}");
}
```

`LogSubscriptionService` spawns a real `kgsm --follow` process; `LogParser` (`../Utilities/`) handles
ISO8601 (Z suffix) and syslog formats differently.

## The event system

Events flow: **an `IEventSource`** → **`EventService`** → **user handlers**. `EventService.Initialize()`
starts the background transport, deserializes `EventWrapper`, resolves the payload type from
`KgsmEventCatalog` (`../Events/CLAUDE.md`), and invokes registered handlers.

**One source, behind an interface.** `EventService` consumes raw envelopes from `IEventSource` and never
learns what produced them. That indirection keeps every consumer's handler code independent of the
transport, and it is worth keeping for that reason.

The source is `EventJournalReader`, reading `/var/lib/kgsm/events/YYYY-MM-DD.ndjson`. Any number of
consumers read the same segments concurrently — a file has no exclusive binding, so there is nothing to
reserve and nothing to tell the engine about. A consumer that was down catches up from its cursor rather
than losing what it slept through, and events it genuinely cannot recover are reported as an
`EventJournalGap` instead of being indistinguishable from no event. The journal directory needs to be
readable, but is tolerated when absent — a host that has never emitted an event has no journal
directory until it does, and the reader picks up the first segment when it appears.

**Cursors.** Position is an `EventCursor` — a segment plus a byte offset — kept by an
`IEventCursorStore` (`FileEventCursorStore`, `NullEventCursorStore`, or the consumer's own; a consumer
that owns a database should store the cursor there, beside what it derives from the events). Delivery
is **at-least-once**: the cursor is stored only past events already dispatched, so a crash costs
re-delivery, never loss — a consumer that persists what it reads must be idempotent.

`EventStartPosition` is a real per-consumer decision, not a default to accept: a consumer that indexes
events needs `CursorOrOldest` so it can rebuild, while one that announces them needs `CursorOrTail` so it
never replays a backlog into a chat channel. When retention has deleted the segment a cursor names, the
reader raises `EventJournalGap` through `IEventService.RegisterGapHandler` and falls back to its
cold-start position — surfacing the discontinuity is what lets a consumer report its history as
incomplete rather than implying coverage it does not have.

**Whole lines.** The byte offset is exact only because **each event is one whole line** — the engine
writes payloads compact for that reason, and only complete lines are dispatched. Anything that rewrites
a segment in place (a log rotator's `copytruncate`) invalidates every cursor into it, which is why
retention deletes whole segments and never truncates one. Every line passes through
`JournalLine.WithoutHole` first (`../../journal/CLAUDE.md`).

**Every event carries its position, and its name.** `IEventSource.EventReceived` and
`IEventService.RegisterRawHandler` both take an `EventPosition` (segment + byte offset) alongside the
envelope. Only raw handlers see it; a consumer that needs it inside a *typed* handler captures it from
a raw handler first (raw handlers run before typed dispatch, for every envelope).

The position says where the event **is**. `AuditId.ForPosition` turns it into
`evt_<segment>_<offset>` — unique by construction, and ordered like the file, so one value works as an
id and as a pagination cursor. That rests on a promise rather than on arithmetic: one line per event,
and segments appended to and deleted whole (`../kgsm-docs/reference/event-journal.md`). **Rewrite a segment and it breaks
silently** — deleting a line shifts every byte after it, and a stored position then resolves to a real,
parseable event of the wrong kind.

`EventWrapper.Id` and `EventPosition.EventId` say what the event is **called** — the UUIDv7 its producer
minted at write time (`../kgsm-docs/reference/event-envelope.md`). `EventService` joins the id onto the position once, for every handler, so a
consumer storing a reference keeps identity and location together and can check one against the other.
**Null is unknown, never a mismatch:** lines written before the field existed are readable for as long
as retention holds them.

## Reading history back (`EventJournalHistory`)

`IEventJournalHistory.QueryAsync` is the other half of the journal: `IEventJournalReader` tails it for
what happens next, this reads back over what it already holds. **There is no index and no cache** — the
point is that nothing can disagree with the record, go stale, or need rebuilding. A per-query scan is
affordable because segments are date-named (a window is narrowed by file *name* before one is opened),
they're read newest-first with an early exit, and each is streamed forward once into a ring buffer the
size of the page.

The three fields that keep an answer honest are not optional decoration. `CoverageFrom` is the oldest
moment the journal can still answer for — a window reaching earlier is answered only from there.
`JournalReadable` separates "cannot see" from "nothing happened". `Truncated` says the scan hit
`KgsmOptions.EventHistoryScanBudgetBytes` and the page is a prefix. A consumer that drops these turns a
partial history into one that reads as complete, which is the exact failure the journal exists to
prevent.

An event with no timestamp is **dropped and logged**, never given a substitute — it cannot be placed in
a time-ordered history, and inventing a moment for it puts something that never happened into the
audit trail.
