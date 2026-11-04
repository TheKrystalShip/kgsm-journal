# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed — concurrent appends keep every line (`TheKrystalShip.KGSM.Journal` 2.5.0-dev.3)

- `EventJournalWriter` writes each line with one `write` on a descriptor opened `O_APPEND`, through
  libc. A `FileStream` in `FileMode.Append` seeks to the end once and writes at that offset, so
  concurrent appends overwrote each other: 300 parallel appends in one process kept 43 to 119 lines.
  Released on its own as `TheKrystalShip.KGSM.Journal` 2.4.1. A test appends 300 lines in parallel and
  reads back all 300.

### Fixed — a file walk sees shallow files before deep ones (`TheKrystalShip.KGSM.Lib` 9.5.0-dev.6)

`IInstanceFiles.Find` and `Search` walk breadth-first, so a walk that stops on `MaxEntriesScanned` has seen
every shallow entry before any deep one. Depth-first, a Project Zomboid server's world save
(`saves/Saves/`, about 310,000 files on hotrod) spent the whole budget before the walk reached the four files of
`saves/Server/`, and a search for its ini found nothing.

### Fixed — a warning-free build (`TheKrystalShip.KGSM.Lib` 9.5.0-dev.5, `TheKrystalShip.KGSM.Journal` 2.5.0-dev.2)

The event history entry and event descriptor documents describe every parameter, the console-run
documentation resolves its `GetConsoleRunTailAsync` reference, and `JournalAccess` reads a state
directory's Unix mode only where the platform has one. The tests assert a captured argument list is
present before comparing it, and declare themselves Linux-only for the file-mode calls.

### Added — the maintenance-window author's display name (`TheKrystalShip.KGSM.Lib` 9.5.0-dev.4)

- **`Instance.MaintenanceWindowsAuthorName`** binds `maintenance_windows_author_name`, null when the
  engine reports no such field.
- **`IInstanceService.SetMaintenanceWindows` takes `authorName`** (last, optional) and passes it as
  `--author-name` beside `--author`. Without an author no name is passed.
- **The engine calls carry no `[Performs]` marks**, and the build references
  `TheKrystalShip.KGSM.ComponentConfig` 4.0.0.

### Changed — a reader scans the ecosystem's journals only (`TheKrystalShip.KGSM.Lib` 9.5.0-dev.2, `TheKrystalShip.KGSM.Journal` 2.5.0-dev.1)

- **`JournalProducer.OrganizationPrefix` is gone and `ScannedPrefixes` is `kgsm` alone.** A state
  directory beginning `tks-` is no longer a journal a reader finds, so `JournalDiscovery` and
  `JournalLayout.ProducerOf` stop at the ecosystem's own components. The `auth.*` event types stay in
  the catalog, so records already written still read.

### Added — `dns.name.moved` in the catalog (`TheKrystalShip.KGSM.Lib` 9.4.0-dev.5)

- **`dns.name.moved`** (the DNS anchor) with `DnsNameMovedData` (`Type`, `Content`, `From`) over
  `DnsNameEventDataBase`. A name-subject fact, success outcome.

### Added — four events producers write, in the catalog (`TheKrystalShip.KGSM.Lib` 9.4.0-dev.4)

- **`dns.name.published` and `dns.name.withdrawn`** (the DNS anchor) with `DnsNamePublishedData` and
  `DnsNameWithdrawnData`, over a shared `DnsNameEventDataBase` (`Name`, `Kind`, `Member`, `Key`). Their
  subject is the new `EventSubject.Name`; both are facts, published a success and withdrawn neutral.
- **`library.renamed` and `library.failed`** (kgsm-api) with `LibraryOutcomeEventData` (`Verb`, `Path`,
  `NewName`, `Error`, `ExitCode`). Both are library facts; a failure is a failure.

### Fixed — the arriving half of a transfer is a fact (`TheKrystalShip.KGSM.Lib` 9.4.0-dev.3)

- **`server.transfer.started` weighs as a fact.** It is the target node's first line for an arriving
  server and the only one that names the node it came from, so an audit view shows it.

### Added — transfer events in the catalog (`TheKrystalShip.KGSM.Lib` 9.4.0-dev.2)

- **`server.transfer.started`, `server.transfer.completed` and `server.transfer.failed`** are in the
  event catalog (`TransferStartedEventData`, `TransferCompletedEventData`, `TransferFailedEventData`),
  registered in `KgsmJsonContext`. The API writes them to its own journal; without a catalog entry the
  event service logged them as unknown and the host conformance check failed on a journal whose newest
  line was one. The started line and a target's failed line name the source's instance and leave the
  local instance empty.

### Added — instance transfer (`TheKrystalShip.KGSM.Lib` 9.4.0-dev.1)

- **Streaming process execution.** `IProcessRunner.ExecuteStreamingAsync` copies a caller's `Stream` to
  the process's stdin and its stdout to a caller's `Stream`, captures stderr under a cap, never buffers
  a part whole, and kills the process tree on cancellation. `ExecuteWithInput` writes a small payload to
  stdin and buffers the output. Both are on `IKgsmCommandExecutor` too. Both interfaces gained members.
- **`IInstanceService` transfer verbs**, each `[Performs]` `server.export` or `server.import` and
  carrying `actor`/`origin`: `ExportManifest`, `ExportPartAsync`, `CommitExport`, `Unlock`,
  `ImportCheck`, `ImportBegin`, `ImportPartAsync`, `ImportFinish`, `ImportAbort`, `ImportList`; and
  `IBlueprintService.BlueprintRequirements`.
- **Records** registered in `KgsmJsonContext`: `TransferManifest` (snake_case, the engine's contract) with
  `TransferInstance`, `TransferPort`, `TransferBlueprint`, `TransferPart`; `ImportVerdict` with
  `ImportRefusal` and `ImportRefusalCodes`; `ImportBeginResult`; `ImportedInstance`; `StagedImport`;
  `BlueprintRequirements`. `TransferExitCodes` names the engine's 59-64 and 25; `StreamResult` and
  `TransferOutcome<T>` carry a verb's outcome.
- **`KgsmActions.ServerExport` and `ServerImport`**, mirroring the engine's manifest.
- **`WatchdogActionResult.Refusal`** with `WatchdogRefusals.NoRoom` and `InstanceLocked`.
- **`server.exported` and `server.imported`** are in the event catalog (`InstanceExportedData`,
  `InstanceImportedData`).

### Added — refused admin attempts (`TheKrystalShip.KGSM.Lib` 9.3.0)

- **`auth.admin.refused` is in the catalog** (Failure, about an account), read into
  `AuthAdminRefusedData` (`Surface`, `Route`, `Account`, `Address`, `Count`) and registered in
  `KgsmJsonContext`. `Address` is `Personal`; `Account` is null when nobody was identified.

### Added — applications and token exchange (`TheKrystalShip.KGSM.Lib` 9.2.0)

- **The auth anchor's application events are in the catalog**, with `ApplicationEventData` (`Id`,
  `Name`, `Client`) registered in `KgsmJsonContext`: `auth.application.changed`, `.removed`,
  `.client.removed` and `.secret.rotated`, all about the access model (`EventSubject.Access`).
- **`auth.token.exchanged` (Success) and `auth.token.exchange_refused` (Failure)**, about the account
  the exchange is for, read into `TokenExchangeEventData`: the client that asked, the application, the
  Discord identity (`Personal`), the client acting for the person, and the refusal's reason. `UserId`
  and `Username` are null when no account holds the identity.

### Added — the organization's services are journal producers (`TheKrystalShip.KGSM.Lib` 9.1.0, `TheKrystalShip.KGSM.Journal` 2.4.0)

- `JournalProducer.OrganizationPrefix` (`tks-`), `ScannedPrefixes` and `IsScanned`: a state directory
  beginning `kgsm` or `tks-` is one a reader scans. `JournalLayout.ProducerOf` and
  `JournalDiscovery` both answer from that one rule, so `tks-auth`'s journal — every sign-in and every
  access change in the cluster — is found beside the ecosystem's own.

### Changed — released (`TheKrystalShip.KGSM.Lib` 9.0.0, `TheKrystalShip.KGSM.Journal` 2.3.1)

Both packages are released as their last prereleases stood; the library builds with
`TheKrystalShip.KGSM.ComponentConfig` 3.2.0. No behaviour changes.

### Removed — the tier model's account events (`TheKrystalShip.KGSM.Lib` 9.0.0-dev.1, `TheKrystalShip.KGSM.Journal` 2.3.1-dev.1)

- `auth.cluster.vouched` and `user.tier_changed` leave the catalog, and their legacy names leave the
  journal's name table.
- `AuthSessionEventData` drops `Tier` and `PeerNode`, `UserAccountEventData` drops `FromTier` and
  `ToTier`, and `AssistantActionDeclinedEventData` drops `Tier`.
- A session ended on somebody else's behalf carries scope `other`.

### Added — who wrote an instance's maintenance windows (`TheKrystalShip.KGSM.Lib` 8.10.0-dev.5)

- **`Instance.MaintenanceWindowsAuthor`** reads `maintenance_windows_author`, the account the engine
  (`3.18.0-rc29`) records beside the windows. A window runs only while its author may do what it does,
  so windows with no author run nothing.
- **`IInstanceService.SetMaintenanceWindows(instance, packed, author, …)`**, marked
  `kgsm:server.windows.write`, writes the windows with `--author`. Windows written through
  `SetInstanceConfigValue` are left with no author, and the author key is refused there.

### Added — the access events (`TheKrystalShip.KGSM.Lib` 8.10.0-dev.4)

- **The auth anchor's access events are in the catalog**, with their payloads registered in
  `KgsmJsonContext`: `auth.assignment.granted`/`.revoked` (`AssignmentEventData`, about the account),
  `auth.role.changed`/`.removed` and `auth.permission.changed`/`.removed` (`AuthorityRecordEventData`),
  `auth.catalog.changed` (`CatalogEventData`) and `auth.service.requirement.approved`/`.revoked`
  (`ServiceRequirementEventData`). Every one carries the authority version it produced.
- **`EventSubject.Access`**: the access model itself — a role, a permission, the catalog, a service's
  requirement — which is never about one account.

### Added — the install nonce (`TheKrystalShip.KGSM.Lib` 8.10.0-dev.3)

`Instance.InstallNonce` (`install_nonce`) is what tells one install apart from another under the same
name. The engine writes it at create and gives one to any instance that lacks it the first time its
info is read. `InstanceUninstalledData.InstallNonce` names the install a `server.uninstalled` removed,
and is null when the instance's library was offline. The catalog classifies it as opaque: it
correlates and shows nothing.

### Added — the engine's actions (`TheKrystalShip.KGSM.Lib` 8.10.0-dev.2)

`KgsmActions` names every action in the engine's manifest (`kgsm/deploy/kgsm.actions.json`) as a
constant — `KgsmActions.ServerRestart` is `kgsm:server.restart` — for callers to check a person against
and for components to name in `[Requires]` when they call the engine as their own service account.
`KgsmActionsTests` reads the engine's manifest from the sibling checkout, or from
`KGSM_ENGINE_CHECKOUT`, and fails naming any action declared on one side only. It also fails when
neither is there, since a comparison that could not be made shows nothing.

Every engine call on `IInstanceService`, `ILifecycleService`, `IBlueprintService`, `IBlueprintFiles`,
`ILibraryService`, `IConfigService`, `IInstanceFiles`, `IInstanceBackups` and `IWatchdogClient` is
marked `[Performs(KgsmActions.…)]`. A component built with `TheKrystalShip.KGSM.ComponentConfig`
3.2.0-dev.2 or later then fails its build at any such call that neither requires the action nor names
it in a check. The package is build-only, and this library gains no dependency from it. Health probes,
UPnP reads and the pure `Render`/`TryParse` stay unmarked.

### Fixed — the hole an unclean shutdown leaves in a segment (`TheKrystalShip.KGSM.Journal` 2.3.0, `TheKrystalShip.KGSM.Lib` 8.9.1)

`JournalLine.WithoutHole` strips the run of NUL bytes a filesystem leaves where it had recorded a
segment as longer without having written the bytes. Every reader of a segment uses it: the history
scan, the tailing reader, the leaf-state replay and the conformance check.

**A NUL is never content** — the writer appends UTF-8 JSON and escapes every control character — and
the hole carries no newline of its own, so it runs straight into the next append and a reader is handed
one line made of both. That event landed, and it is read rather than discarded with the zeros. The
bytes the hole stands in for are gone and nothing here invents them; zeros left *between* two partial
records are a torn write, stay unparseable, and are reported. The conformance check names a line that
is nothing but hole for what it is, rather than reporting its producer for bytes it never wrote — and
the leaf-state replay now sees the `leaf.ready` a restart wrote against a hole, instead of reporting
components as still broken over faults that restart had already wiped.

**A standing fault in a segment is reported once.** A segment is never rewritten, so a line that will
not parse meets every later query at the same offset. `EventJournalHistory` remembers the positions it
has described: the first encounter carries the exception, the repeats go to Debug. Measured here at
146,000 identical entries in a day from three faults — a log reporting one fact so often that nothing
else in it could be found.

### Added — a leaf's fault read back with what it said, and how it ended (`TheKrystalShip.KGSM.Journal` 2.2.0, `TheKrystalShip.KGSM.Lib` 8.9.0)

`LeafState.Read` replays a producer's newest segment into a `LeafStateReport`: each component still
broken with the `Detail` the leaf wrote and when it reported it (`LeafDegradation`), the components
whose last transition is a reported recovery, and the components a `leaf.ready` wiped before anything
re-observed them.

**The last two are different facts, and a consumer announcing a recovery needs them apart.** A resident
daemon restarted in the middle of an outage writes a ready line with the fault still true; reading that
as a recovery announces the outage over while it continues. A component the segment never mentions is
in none of the three sets, so a fault reported before the segment boundary reads as unknown rather than
as either outcome. `DegradedComponents` is the same replay reduced to ids.

### Added — the run of guesses that locked an account (`TheKrystalShip.KGSM.Lib` 8.8.0)

`auth.locked_out` classifies the one authentication *failure* that is worth a row. Everything else in
the `auth_*` family records something that worked; this records somebody working through passwords
against a real account, which is the fact an operator reviewing a week of access most needs and the
one nothing has ever recorded.

**It is emitted when the lock begins, never on the attempts it then refuses.** An account under
attack is retried immediately, so a row per refused attempt would produce exactly the flood that
buries the record it is made of. One run of guessing is one row however long the run is, and
`FailedCount` says how far it got.

**A wrong username never reaches it.** There is nothing to lock, so the event names an account that
exists — which is what separates it from a failed attempt and is why it carries `Identity` while
saying nothing about attempts that matched nobody.

`Until` is when the account can be tried again, so a reader can tell a lock that has since expired
from one still standing without consulting the store.

### Changed — accounts are authored by whoever holds them

The account-subject events are no longer described as the Control Panel's own. A host that holds its
own accounts writes them from its API; a cluster whose accounts are held by an auth anchor has the
anchor write them, into the anchor's journal. The classification is unchanged and the wire shapes are
unchanged — a reader establishes the producer from the journal a line was read out of, so both are
attributed correctly without either naming itself in a payload.

### Added — an offer, and how it ended (`TheKrystalShip.KGSM.Lib` 8.7.0)

`reactor.proposed` and `reactor.resolved` classify the two facts a reactor rule running in propose
mode produces: an action staged for a person to confirm, and the end that offer reached.

**Four events, because each is a separate immutable fact.** A rule decided; an offer was put to a
person; that offer ended; the reactor performed something itself. Collapsing any pair makes a real
case unrepresentable — *"it decided and the action failed"*, or *"it offered and nobody answered"*,
which is the one an operator most needs when reviewing a week of decisions.

`ReactorResolutions` carries the four ways out, and they are kept apart deliberately: a rule whose
offers are mostly `confirmed` is a candidate for acting on its own, one whose offers are mostly
`dismissed` has a wrong condition, one whose offers mostly `lapse` is unwanted, and one whose offers
mostly go `no_longer_applicable` speaks too early. A consumer folding the last three into "not
confirmed" throws away the only signal separating them.

**The resolution says what the person did; `Ok` says what the action did.** `Ok` is nullable and
absent whenever nothing ran, which is three resolutions out of four — reading a missing `Ok` as
`false` reports every dismissal as a broken action.

**A proposal's handle is spelled `ProposalHandle` on the wire.** A bare `Handle` already means a
person's account handle, classified `Identity`/`Personal`; this is an opaque capability that names
nobody. One field name cannot carry two classifications, and a consumer meeting both would treat
whichever it saw first as the answer for both.

`AnsweredBy` is classified `Personal` beside `RuleAuthor`, and for the same reason: it names a natural
person, so a surface listing resolutions to a room of players has to be able to withhold it.

### Added — the reactor's judgments read back typed (`TheKrystalShip.KGSM.Lib` 8.6.0)

`reactor.decided` and `reactor.acted` are classified, so a consumer that registers a handler for one
gets a payload class instead of a `JsonElement`. `ReactorEvents` names them, `ReactorEventFields`
names every field once, and `ReactorOutcomes` carries the six spellings a verdict can have.

**Service-subject, not instance-subject**, for the same reason the assistant's own events are: a
decision is something the host *noticed* about a server rather than something that happened to one,
and its subject is not always a server at all — a threshold episode is about a sensor. A consumer
routing on the subject would file a judgment in the same list as the events it was judging.

`RuleAuthor` is who shaped the rule, as a stable `provider:name` username, classified as naming a
natural person — the one personal field the reactor writes, and the reason a surface listing decisions
to a room of players does not print the operator behind each rule beside it. **Null is a real
state**: a rule the build ships, or one hand-written over SSH, carries no identity, and there is no
fallback to the OS user anywhere in this ecosystem.

**Three of the six outcomes are not verdicts about the world.** `fired` and `settled` report what
the rule decided; `suppressed`, `ceilinged` and `superseded` report that it decided *yes* and the gate
held it back. A consumer counting how often a condition was true must count those; one counting how
often anything happened must not.

The binding is checked against **lines the leaf actually wrote**, including one written before
`RuleAuthor` existed — the producer is a different repository writing from its own copy of the field
names, so nothing in this solution would otherwise fail when the two spellings part company, and the
failure is silent: a property that does not bind keeps its initialised value and reads as an ordinary
empty answer.

### Changed — an unasked-for stop is danger (`TheKrystalShip.KGSM.Lib` 8.5.0)

`server.crashed` carries `danger`, the same weight as the give-up it may lead to. A server going down
unasked is the fleet losing a service, and the supervisor being on its way to bring it back does not
make that routine. Which of the two a row is stays readable from its name, so nothing is lost by them
sharing a colour.

### Added — a payload class names its own event (`TheKrystalShip.KGSM.Lib` 8.4.0)

`KgsmEventCatalog.NameOf<TData>()` answers what the event carrying a payload class is called — the
inverse of the binding `Describe` reads forwards. A consumer that dispatches on a typed handler
already says which event it is for, so it derives the name instead of spelling a literal beside the
registration, where the two could drift apart with nothing to notice. A class shared by several
events names none of them and throws, since its producer's own constants are what say which.

### Changed — one dotted vocabulary (`TheKrystalShip.KGSM.Journal` 2.1.0, `TheKrystalShip.KGSM.Lib` 8.3.0)

Every event is named the way a reader groups it: domain first, past tense, an outcome as its own
segment — `network.ports.opened`, `server.update.failed`, `backup.created`. The name's hierarchy is
what a consumer keys its presentation off, so the vocabulary's own structure replaces the table that
used to translate an engine name into a display one.

`LegacyEventNames` resolves a name an earlier build wrote, applied where a line is read. Retention
holds a segment for months, so both spellings are on disk for one retention period after a producer
cuts over; resolving it once, at the read, is what keeps every consumer above working in a single
vocabulary. The table has an end date and goes with the last legacy line.

An unclassified event's subject is read off its namespace rather than guessed from a prefix, so a
`blueprint.*` nothing has described is still known to be about a blueprint.

### Added — the catalog declares how much each event matters (`TheKrystalShip.KGSM.Lib` 8.2.0)

`EventDescriptor.Severity` sits beside `Outcome`, so the engine's vocabulary declares both in the one
registry that already says what an event is. A producer stamps an envelope from `Describe(type)`
rather than hand-tagging each call site, which is what keeps the weight of an event in the same place
as its payload type and its fields. Anything not named is `info`.

### Added — the read half sees what the producer said (`TheKrystalShip.KGSM.Lib` 8.1.0)

`EventWrapper` and `EventHistoryEntry` carry `Severity`, `Outcome` and `Summary`, so a consumer
reading the journal gets what the producer said about its own event rather than only the payload.
Carried through untouched and null when the producer said nothing — a reader substituting a default
would be putting its own judgement on the line under the producer's name.

### Changed — an event says how much it matters, how it went, and what happened (`TheKrystalShip.KGSM.Journal` 2.0.0, `TheKrystalShip.KGSM.Lib` 8.0.0)

The envelope carries three fields beside the payload: `Severity` (`info`/`warn`/`danger`), `Outcome`
(`success`/`failure`/`neutral`) and `Summary`, one line of prose written at emit time. A reader renders
an event it has never heard of from those three, so no consumer holds a list of event types and none
can be missing one. All three are optional — a producer that says nothing is quiet rather than
malformed, and absence is unknown.

`EventName` is the type an event is named by, and it has no conversion from `string`: a raw literal
cannot reach the writer, so a producer's names are declared once rather than spelled at each call
site. It validates shape — dot-separated segments of lowercase letters, digits and underscores — and
never membership, because a registry of valid names would mean every new leaf event needed a release
of this package before it could be written.

`EventSeverity` and `EventOutcome` are enums with one wire spelling each, defined here and consumed by
every producer. A scale restated per repo is two vocabularies, and a reader that meets both is holding
a translation table.

Conformance checks the new fields' spelling and stops there: what a particular event deserves is the
producer's judgement, and a rule asserting it would put one producer's policy inside a package every
other producer compiles against. The reader understands envelope versions 1 and 2, because a journal
holds what earlier builds wrote for as long as retention keeps it.

### Changed — an actor names a provider as well as a name (`Journal` 1.11.0, `Lib` 7.0.2)

`envelope.actor` reports a bare name. An actor is `provider:name`, the form every reader splits it
back into; a name with no provider in front of it is the shape an OS username takes, and the OS user
owns the process rather than asking for the action — so it names the wrong principal on an audit
record even when the string is a real login on that host. A provider the reader does not recognise
is still conformant: which providers a host has is its own configuration, and keeping the name
rather than coercing it is the never-fabricate rule applied to provenance.

The provenance parameters across `IInstanceService`, `ILifecycleService`, `ILibraryService`,
`IBlueprintFiles`, `IEventManagementService` and `KgsmProvenance` state what a null actor now means:
the event records unclaimed. Documentation; the behaviour is the engine's.

### Fixed — the supervision phase vocabulary states every phase the daemon reports (`Lib` 7.0.1)

Documentation only; no behaviour changes.

- `WatchdogInstanceState.Phase` names `maintenance` alongside `running`, `restart-pending`, `stopped`,
  `failed` and `unknown`, and says what a reader should do with it: the server is deliberately down
  while a leaf works on it, `Desired` is still `running`, crash-restart is suppressed for as long as the
  park holds, and `Restarts` is untouched. Rendering it as stopped or as failed says something the
  daemon does not — nobody asked for the server to be down, and nothing about it went wrong. The XML
  docs ship inside the package, so this is the vocabulary a consumer reads.


### Changed — an instance states its maintenance in one grammar (`Lib` 7.0.0)

**Breaking.** An instance states everything it does on a clock in one value, `Instance.MaintenanceWindows`
(`maintenance_windows`):

```
daily@05:00/backup;weekly.sun@04:00/backup,update,restart
```

A window is a schedule, a `/`, and the tasks it runs; windows are separated by `;`. An instance holds a
list of them. Tasks inside one window are ordered and dependent; windows are independent appointments
that carry no state between them, so making window 2 rely on window 1 means merging them.

- **`MaintenanceWindowParser`** (`TheKrystalShip.KGSM.Core.Scheduling`) is the ecosystem's one
  implementation of the grammar and its one validator — `Parse` for an instance's whole value,
  `ParseWindow` for a single expression, `Format` to pack windows back. It returns one
  `MaintenanceWindow` per expression written, **each carrying its own validity and parse error**, so an
  unreadable window disables itself and leaves the rest of the list firing — and it is returned invalid
  rather than dropped. Nothing here throws for anything a person can type.
- **Tasks run `backup` → `update` → `restart`**, whatever order they were written in, and duplicates
  collapse. A backup taken after an update archives the new build instead of the rollback point.
- **`ScheduleClock`** times a window. Appointments (`daily@HH:MM`, `weekly.<dow>@HH:MM`,
  `monthly.<dom>@HH:MM`) are read in the instance's `Timezone`, comparing in UTC so a daylight-saving
  transition moves a fire by the offset it changed and never by a day; a time the clock skips fires at
  the jump, a time it reads twice fires the first time, and `monthly.31` fires on a shorter month's last
  day. Intervals (`10m`–`30d`) are epoch-aligned to whole multiples from `1970-01-01T00:00Z`, so they
  need no stored anchor and every host answers identically. `NextFire` and `NextFires` answer "strictly
  after this instant"; an invalid window has no next fire.
- **`Instance.AnnounceMaintenanceMessage`** / **`Instance.AnnounceMaintenanceCancelledMessage`**
  (`announce_maintenance_message`, `announce_maintenance_cancelled_message`) — a window is announced,
  not a task. `Instance.Timezone`, `BackupRetention` and `AnnounceLeadMinutes` are unchanged.

### Added — the watchdog park primitive (`Lib` 7.0.0)

- `IWatchdogClient.BeginMaintenanceAsync` / `EndMaintenanceAsync` park an instance: stopped, but
  desired-state stays `running`, so crash-restart is suppressed for as long as the park holds rather
  than switched off, and the failure streak and give-up latch come out of the park as they went in. The
  daemon unparks on its own once its timeout expires, so a leaf that dies mid-sequence costs a window
  and never a server.
- `origin` on `StartAsync` and `StopAsync`, matching `RestartAsync` — a leaf-driven lifecycle call is
  attributed to the leaf as `system:<origin>` instead of to whoever owns the process.


### Added — an instance carries what it announces before a scheduled restart (`Lib` 6.3.0)

Binds kgsm 3.18.0-rc9. Read by `kgsm-scheduler`; inert for a game whose blueprint declares no
`broadcast_command`.

- `Instance.AnnounceLeadMinutes` — comma-separated minutes, e.g. `15,5,1`. Null or empty announces
  nothing, which is the default.
- `Instance.AnnounceRestartMessage` — `{minutes}` and `{instance}` are substituted. The result is then
  substituted into the game's own broadcast template, so these are two separate steps with different
  placeholders and different owners.
- `Instance.AnnounceRestartCancelledMessage` — what is said when an announced restart is abandoned.


### Added — a server announces to the people playing on it (`Lib` 6.2.0)

Binds kgsm 3.18.0-rc7, which owns the console write. This is the single C# entry point to it.

- `IInstanceService.Announce(instanceName, message, actor, origin)` runs `instances announce`. The
  engine substitutes the message into the game's blueprint-declared template and sends the result;
  nothing here builds the console command, so there is one implementation of that substitution and it
  is the one that actually runs.
- `Instance.BroadcastCommand` carries the engine's `broadcast_command`. Empty means the game declares
  none on the STDIN console — which is not the same as "this game cannot be announced to", since a
  game whose broadcast lives on RCON or an in-game admin console also declares none here.
- `BroadcastCommand.IsSupported(template)` answers whether the action is available, so a surface can
  gate a button without shelling anything. A template carrying no `{message}` placeholder reads as
  unsupported: the engine would send its bare verb and drop the text, so reporting it as usable would
  promise a send that never carries the message.
- A message containing a line break throws at the call site. A console reads one command per line, so
  a second line would deliver a command nobody issued — the engine refuses it too, and failing here
  means no malformed argument is spawned at all. Prose punctuation is passed through untouched.
- `InstanceAnnouncementSentData` deserializes `instance_announcement_sent`, carrying both the message
  as written and the console command it resolved to.

**A successful result means the engine wrote to the console, never that a person read it.** Nothing
above this layer can observe delivery, so no surface may report the message as seen.


### Added — an instance's id and the name a person reads it by are two things (`Lib` 6.1.0) — BREAKING

The id is auto-generated at install, path-safe and immutable; the display name is decoration that
any surface can change at any time without breaking anything keyed on the id. Binds kgsm 3.18.0.

- `Instance.DisplayName` carries the engine's `display_name`. **Never null and never empty:** an
  instance with no label of its own reads as its `Name`, which is the answer the engine already
  gives for a config that sets no label, and the one it cannot give for an instance whose library is
  offline — that payload states nothing at all, and a blank label would render as a nameless row.
- `IInstanceService.SetDisplayName(instanceId, displayName, actor, origin)` renames. Nothing on disk
  moves, so it is safe on a running server, as often as somebody likes.
- `InstanceDisplayNameChangedData` binds the engine's `instance_display_name_changed`, carrying the
  id plus both labels — in full, unlike `instance_config_changed`'s key-only payload, because a
  label is text chosen to be read and a consumer holding a stale one has everything it needs to
  re-render. Classified in `KgsmEventCatalog`, so it dispatches.
- `InstanceDisplayName.Sanitize` reduces a label to the one line it is: control characters go,
  surrounding whitespace is trimmed, and everything printable — quotes, backslashes, backticks,
  emoji — is stored exactly as typed. `SetDisplayName` applies it. A tab would truncate the value
  where the engine's config-to-JSON render splits key from value, and a newline would make the rest
  of the label parse as further config keys, one of which is `name`.

**Breaking, and deliberately loud:** `IInstanceService.Install`'s `name` parameter is now
`displayName` and means the label, matching the engine, where before it named the instance. A caller
that needs to choose the identifier passes the new trailing `id`, which the engine validates against
`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` and the existing roster. `GenerateId`'s second parameter is
`id` and rides on `--id`; it sent `--name`, which that verb no longer takes. Every call site passing
an identifier fails to compile rather than silently installing a server under a generated id and
labelling it with what the caller meant as the name.

### Changed — an instance whose disk is away reports unknown, not stopped (`Lib` 6.0.0) — BREAKING

The engine measures an unmounted library as an absence and says so; the library was reading that
absence as a set of confident defaults. Four properties change type so it cannot:

- `InstanceRuntimeStatus.Status` is `bool?`. The engine emits `null` for an instance it could not
  read, and the old `bool` landed on `false` — telling an operator their server is down when what
  happened is a disk came out.
- `Instance.Runtime` is `InstanceRuntime?`. The offline payload omits `runtime` entirely, and the
  old non-nullable enum landed on `Native`'s zero: a consumer would ask the watchdog for a
  container's run state and get back a confident wrong answer.
- `InstanceRuntimeStatus.Version.Current`, `.Configuration.Runtime` and `.Resources.DiskUsage` are
  nullable, because the engine sends `null` for each of them and a non-nullable `string` holding
  `null` is a promise the type does not keep.
- `ILibraryService.Remove` takes `drainTo` before `actor`/`origin`.

`Instance.Blueprint` and `InstanceRuntimeStatus.LibraryState`/`.Configuration.Library` bind the
fields the engine already sends. `Blueprint` is now settable and reads the engine's own `blueprint`
field — the name comes out of the instance registry, which is on this host rather than on the
absent disk — falling back to deriving it from `BlueprintFile`. That derivation strips `.bp.yaml`
as a unit, so a unified blueprint reads `factorio` rather than `factorio.bp`.

`Instance.LibraryState` and `InstanceRuntimeStatus.LibraryState` carry the engine's always-present
`library_state` as `InstanceLibraryState` — `Online`, `Offline` or `Unregistered`, three states
where `LibraryState` has two, because an instance can also sit under a root this host holds no
entry for. This is the field that says why the rest of an offline instance is empty: `Name`,
`Blueprint`, `WorkingDir`, `LibraryDir` and `Library` are real, and everything else is its default
because nothing read one.

### Added — moving an instance between libraries (`Lib` 6.0.0)

`IInstanceService.Move(instance, library, skipSpaceCheck)` runs `kgsm instances move`, and
`ILibraryService.Remove(name, drainTo:)` runs `libraries remove --drain` — move every resident
instance into a target library, then deregister. Together they are how a disk is emptied before it
is taken out.

Both are minutes of copying, not requests: `KgsmTimeoutOptions.Move` (2 hours) is sized for a
populated drive going out rather than for one server, and a caller should drive either as a job.
**The move starts the instance once on the new path to confirm it runs there**, so an
`instance_started` and an `instance_stopped` land partway through with no bracket around them. A
surface reading run-state off those alone shows the server running mid-move; the operation's own
bracket is the caller's to keep.

`--drain` and `--force` are mutually exclusive and the engine owns that rule — the library sends
both and lets the refusal come back, so there is one answer to it and the surface shows the
engine's words.

`instance_moved` is classified in `KgsmEventCatalog`, dispatching into `InstanceMovedData`
(`FromLibrary`, `ToLibrary`). Both libraries are named because a reader that learns only the
destination cannot tell which disk just got its space back. `InstanceInstalledData` gains
`Library`, so a record of an install can say which disk the server went onto.

### Changed — placement is a named library, not a path (`Lib` 5.0.0) — BREAKING

`IInstanceService.Install` takes `library` where it took `installDir`, and passes it as
`--library`. The engine has no `--install-dir` flag: every instance is placed in a registered
library, so there is no path escape to model. A caller that still hands the argument a path gets
the engine's unregistered-library refusal — a loud failure at the engine rather than a silent
install somewhere nobody enumerates.

`library` is optional, and null is the ordinary case: KGSM resolves placement from
`default_library`, else from the sole registered library, else refuses. Deciding it in the library
would put a host's placement policy in every consumer.

### Added — `ILibraryService`, the typed surface over `kgsm libraries` (`Lib` 5.0.0)

`List`, `Add`, `Remove` and `Rename`, reachable as `IKgsmClient.Libraries`. This is the only route
a C# project has to library management; nothing else shells `kgsm libraries`.

`Library` carries `Name`, `Path`, `State`, `FreeBytes`, `TotalBytes` and `InstanceCount`.
**An offline library reports null capacity, not zero** — an unreachable root was never measured,
and a zero would read as a full disk. Its `InstanceCount` is still answered, being read from the
instance registry rather than from the disk. `List()` returns null on a failed read and an empty
list for a host with nothing registered: a surface that collapses the two offers "no libraries" as
a fact it never read.

`Instance` gains `LibraryDir` (the absolute root the instance sits under) and `Library` (the
resolved library name, or `unregistered` when its root matches no registered library).
`InstallDir` is unchanged and unrelated — it is the game-binaries subdirectory of `WorkingDir`.

`library_added` and `library_removed` are classified in `KgsmEventCatalog` under a new
`EventSubject.Library`, so they dispatch into `LibraryAddedData`/`LibraryRemovedData` instead of
being dropped as unknown. A library is its own subject: it is registered before anything lives in
it and survives every instance leaving it.


### Added — a nullable answer for a stringly-typed integer (`Lib` 4.48.0)

`JsonStringToNullableIntConverter` maps a value that is absent, empty or unparseable to `null` rather
than `0`. The existing `JsonStringToIntConverter` answers `0`, which is correct for a key whose zero
means something — `memory_cap_mb="0"` is KGSM's spelling of "uncapped" — and wrong for one where zero
would be a fabricated answer rather than a missing one.

Apply it per-property. It is deliberately not registered globally: which of the two answers is honest
depends on what the key means, and only the property knows that.

**4.47.0 carries four `Instance` properties this does not** — `ObservedRamMb`, `ObservedRamPeakMb`,
`ObservedWindowDays` and `ObservedUpdatedAt`. A published version cannot be replaced, so a consumer that
pinned 4.47.0 still compiles against them; nothing on the feed depends on it. What an instance has been
measured to hold is kgsm-monitor's to serve, from the footprint it accumulates, rather than a property of
the engine's instance model.

### Added — a start can override the node's memory gate (`Lib` 4.46.0)

`ILifecycleService.Start` takes `bool force = false`, which passes `--force` to
`kgsm lifecycle start`. KGSM refuses a start that would leave the node with less free memory than its
configured floor, comparing the instance's own `memory_cap_mb` — or its blueprint's advisory
`min_ram_mb` — against what the node reports available. That fallback is a vendor estimate and can
overstate what a game really uses, which is what this exists for.

The flag is appended only when asked for, and defaults to false, so a caller that does not request it
keeps the protection. Provenance and force travel by different channels — environment and argument —
so asking for one never drops the other.

It does not create memory. Forcing a start the node genuinely cannot fit invites the OOM killer,
which may take down a different server, or the watchdog supervising them all.

### Added — the run clock reaches stopped instances (`Lib` 4.45.0)

`IWatchdogClient.GetRunTimesAsync()` reads the daemon's `GET /runtimes`: `WatchdogRunTimes` (name,
`SpawnedAt`, `LastExitedAt`) for every instance it can date.

This exists because `ListAsync` cannot answer the question it looks like it answers. An instance
leaves the daemon's supervised table when it stops, so a list walk reports nothing at all for the
instances that are stopped — which is exactly when "how long has this been down" is asked. The new
call unions the supervised table with the durable run ledger, so a stopped instance still carries the
end of its last run.

Prefer it over `ListAsync` for any run-duration question; `ListAsync` stays the supervision-state read.

### Added — the watchdog's instance state dates the run (`Lib` 4.44.0)

`WatchdogInstanceState` carries two new timestamps, so a consumer can say how long an instance has
been up, or how long it has been down, from the run-state authority itself rather than by correlating
a second source:

- `SpawnedAt` — when the current run was spawned. Null when nothing is running, and null for an
  instance the daemon adopted rather than spawned. The daemon persists it alongside the phase, so it
  is an uptime rather than a "seen since": restarting the watchdog does not reset it.
- `LastExitedAt` — when the last run ended, from the daemon's durable run ledger. Null for an
  instance with no recorded runs. It is the run's own last output, not the moment the supervisor
  noticed the cgroup had emptied.

Both are additive and nullable; a daemon that does not report them deserializes to null.

### Fixed — a segment's top rows were selected by file order and served in id order (`Lib` 4.43.0)

**A silent skip.** Each segment is streamed forward once with matches kept in a bounded ring, and
the ring dropped the oldest as it went — right only while id order and file order agree. The
assembled page is then sorted by `(timestamp, id)`, and a row the ring already discarded is one that
sort never sees and no page ever serves. Nothing counts it, so a walk just comes up short.

The ring is now a bounded heap ordered by the page's own comparator, `PageOrderAscending` — one
definition, used by the scan to decide what to keep and by the page to decide what order to serve.
Same single forward pass, same O(page) memory, one comparison per match.

This also makes the read correct for a segment whose lines are not in timestamp order, which the
final sort was documented as covering and could not.

Measured: four events sharing one millisecond, paged one row at a time, served three.

### Changed — an audit id is the line's own name when the line has one (`Lib` 4.43.0)

`AuditId.ForLine` prefers the producer's minted id (`evt_<uuidv7>`) and falls back to the positional
form for a line that carries none. A position is right only while a segment is appended to and
deleted whole (conformance §2·l); a name survives a rewrite, so a row keeps its identity and the
rewrite surfaces as a position that no longer resolves.

The shape is checked rather than trusted — an id this ecosystem did not write cannot be assumed
unique or ordered, and a malformed one falls back to the position rather than putting a duplicate or
a mis-sort into a page.

**Audit row ids change**, for lines written since producers began minting ids. Nothing persists
one, so there is no stored migration; the `/audit` cursor is opaque and its encoding has changed
before. A named id carries no producer prefix — a minted id is already unique across every journal on
the host, which is what the prefix compensated for.

**Every derivation of an event's id must make the same choice**, or one event served two ways —
pushed live and found in history — comes back with two ids. `ForLine` takes the id as an argument so
neither caller can quietly opt out.

Ordering survives: a UUIDv7 is time-ordered, so within a producer these sort the way the journal
does. The tie-break's real requirement is only that both sources of a merged page agree on a stable
total order — a caller merges this history with rows that have no journal position at all, so the
comparison can never be positional. Mixed forms page cleanly, which is what lets the two coexist for
the 90 days retention holds a line written before ids existed.

### Added — a reader receives the line's own id (`Journal` 1.10.0 / `Lib` 4.42.0)

`EventWrapper.Id` carries the id off the envelope, and `EventPosition.EventId` carries it beside the
location, so a raw handler is handed both without re-parsing a line it has already been given.
`EventService` joins them once, for every handler, because the transport can only know a location
without parsing while the name lives in the line.

**Nothing consumes it yet.** A consumer that stores a reference can now store identity *and* location
together, which is what turns the silent failure into a loud one: seek by the position, compare the
id, and a rewritten segment stops resolving to a real event of the wrong kind.

Null is unknown and never a mismatch, on both. Every line written before the field existed stays on
disk for as long as retention holds it, and so does every line from a producer whose shell cannot mint
an id.

`EventPosition.EventId` takes part in equality, like `Producer`: two values that disagree about which
event this is are two different assertions. Addressability is `IsKnown`, which reads the segment
alone — never a comparison against `None`.

### Added — `envelope.event-id-shape`, the fourteenth conformance rule (`Journal` 1.10.0)

An id, when there is one, is a lowercase hyphenated UUIDv7 with the RFC 4122 variant.
`JournalConformance.IsWellFormedEventId` is the judgment, public because a consumer comparing a stored
id needs the same answer the checker gives.

Stricter than `Guid.TryParse`, in the two directions that matter. **Case**, because every store that
keeps an id compares it as text, so an uppercase spelling of the same id is a different string and a
producer writing one would look like a producer writing different events. **Version**, because a v4
parses perfectly and silently loses the time-ordering the format was chosen for.

Null stays absence; an empty string stays `envelope.absent-spelling`'s single finding rather than
becoming two findings for one defect.

Measured against the live host before shipping: 588 lines across all ten journals, every id well
formed, no rule fired.


### Fixed — `Journal` 1.9.1 / `Lib` 4.41.1 carry the id write that 1.9.0 was supposed to

**`TheKrystalShip.KGSM.Journal` 1.9.0 on the feed does not contain the `Id` write.** It was packed from
a Release tree that predated the change: `dotnet pack` builds, but it honours the up-to-date check, and
the intervening `dotnet test` runs had only rebuilt Debug. A published version is immutable, so 1.9.0
cannot be corrected — **do not use it**; it emits no id and is otherwise identical to 1.8.1.

`scripts/publish-packages.sh` now builds `--no-incremental` before packing, so a package can no longer
be assembled from output older than its source.

### Added — every line the shared writer emits now carries its own id (`Journal` 1.9.0, `Lib` 4.41.0)

`EventJournalWriter.Compose` mints a **UUIDv7** per line (`Guid.CreateVersion7()`), written beside `V`.
Every .NET producer inherits it by re-pinning; none changes a line of its own code.

Minted, never derived from the content — two identical events in the same second are two events, and a
digest over the line folds them into one, which is the defect the engine's own index has. v7 rather
than v4 so an id sorts the way the journal does.

The tests assert the version nibble and the variant bits rather than "is a guid" (a v4 would pass the
looser check and lose the ordering the choice was made for), that two identical events get different
ids, and that a line carrying an id **still conforms** — which closes the §2·m ordering constraint end
to end.

### Added — the contract knows the line's own id, before anything writes one (`Journal` 1.9.0)

`JournalConformance.OptionalFields` carries `Id`. Nothing emits one yet, and that ordering is the
point: the checker enforces `envelope.unknown-field` against this list, so a producer that shipped an
id first would have every line it writes reported as having invented a field — and the host
conformance check runs against the live journals.

The field is defined by **`event-conformance-plan.md` §2·m**: UUIDv7, lowercase hyphenated, minted by
the producer and never derived from content, optional forever. A line written before it existed is on
disk for as long as retention holds it, so **absent means unknown, never a mismatch**. Adding it does
not bump `V` — a reader that has never heard of `Id` reads such a line exactly as it always did.

### Added — the rule every stored position depends on (`Journal` 1.9.0)

**`event-conformance-plan.md` §2·l: a segment is appended to and deleted whole, never rewritten.**
Every durable reference to an event on this host is a byte offset into a named segment, and that only
works while lines do not move. The rule existed as prose in three doc comments and as an obligation
nowhere.

`Retention_LeavesEveryKeptSegmentByteIdentical` enforces it at the pruner, asserting the **bytes** of a
kept segment over several lines rather than that the file still exists — `copytruncate` and dropping
the first N lines both leave the file there.

There is deliberately **no `ConformanceRule` id** for it. A line-level checker reads a journal as it
is now and holds no baseline, so it could never produce that finding, and a rule that can never fire
is a check that exists only in a list.

### Changed — `EventPosition` no longer promises what it cannot (`Lib` 4.40.0)

Its remarks asserted that an event's position never changes. That is true only while §2·l holds, and
the doc now says so — along with what breaks when it does not: a stored position resolving to a real,
parseable event that is simply not the one it named.

### Added — a backup's reason and retention (`Lib` 4.40.0)

`InstanceBackup` carries `Reason` and `Retention`, and `IsPinned` resolves the latter so a consumer
never compares the string. They are deliberately separate: the reason is a fact fixed when the
archive was captured, the retention is a policy an operator revises, and a slot they shared could
never diverge. `BackupReason` and `BackupRetention` hold the closed vocabularies the engine accepts.

`CreateBackup` takes a `reason` and a `retention`, validated here so a typo costs no round trip and
never lands as an unrecognised word in the one record of what a backup is. `PinBackup` and
`UnpinBackup` change the policy afterwards; `PruneBackups` keeps N *prunable* backups, since the
engine skips pinned ones without counting them.

`Reason` is null when the manifest records none. That is **unknown** and never a guess — a backup
written before the field existed cannot be identified after the fact, and a surface must say so.
A null `Retention` is prunable, which is what the field's absence means and the behaviour that
backup already had.

`InstanceBackupsPrunedData` gained `Pinned`, so a sweep that removed nothing because everything was
protected is distinguishable from one that found nothing to remove. `InstanceBackupPinnedData` and
`InstanceBackupUnpinnedData` are the two new events, classified in `KgsmEventCatalog` and registered
in `KgsmJsonContext`.

### Added — an instance's whole configuration, with what may be changed (`Lib` 4.39.0)

`IInstanceService.GetInstanceConfig(instance, settableOnly)` reads every key, its value, and whether
`SetInstanceConfigValue` will accept a change to it, as `InstanceConfigEntry`.

The settable flag is the engine's own judgement, from the same rule the setter applies — so a surface
offering to change a key marked settable is offering something the write path accepts, and there is no
second copy of the rule to keep in step. Null means the read failed: an instance always has a
configuration, so an empty list is never the honest answer to one.

### Added — the host's ports and port conflicts, typed (`Lib` 4.38.0)

`INetworkService.ListUsedPortsDetailed()` and `FindConflictsDetailed()` read the engine's
`--json` forms into `HostPort` and `PortConflict`.

A listening socket the scan could not attribute carries a null `Process` — the port is still a
measurement, only who holds it is unknown. A host with no conflicts deserializes to an empty list,
the same shape a host with findings produces, so nothing above this layer recognises a sentinel
word (or a progress message) to learn which it got.

Both return null when the scan could not be made, rather than the empty list that means it ran and
found nothing. It matters most on the conflict read: no conflicts is the ordinary answer, so
collapsing a failed scan into it would report "all clear" on a host nobody managed to check.

`ListUsedPorts()` and `FindConflicts()` are unchanged: they return the human rendering and the
exit-code signal, which is what tells "nothing is listening" apart from "the read failed".

### Added — the assistant's own event contract (`Lib` 4.36.0)

The read half of what the assistant leaf reports about its own conduct: five event types, their
payload classes, their catalog descriptors and their `KgsmJsonContext` registrations.

**Deliberately not a log of what the assistant did.** Every mutation it performs runs through this
library with provenance attached, so the engine's journal already records it, attributed to the person
who asked — 98 such events on the reference host. A copy here would be a second answer able to
disagree. What these record is the opposite: **the turn that did not act**, which leaves the engine's
record empty because from its side nothing occurred.

- `assistant_claim_corrected` — a reply described an action the turn never took, or a lookup it never
  made. The only measurement of the deployed model's fabrication rate on real prompts; the benchmark
  scores the same checks against a fixed corpus, which is a different question.
- `assistant_action_declined` — somebody reached past their tier. Authorization only: the
  blast-radius refusals are loop guards firing on ordinary model over-eagerness.
- `assistant_action_proposed` — a mutation is staged and waiting on a person. Carries no handle; the
  handle is the capability that redeems the action.
- `assistant_blueprint_authoring_started` / `assistant_blueprint_authored` — brackets around a run
  whose probe install and uninstall the engine records in full, plus the outcome, which on a failed run
  is the only event either way.

**`AssistantEventContractTests` covers what the catalog's own drift tests cannot.** Those compare a
descriptor's fields against the payload's *C# property names* — the right question for classification
and the wrong one for binding. A property whose `[JsonPropertyName]` drifts from the shared constant
keeps its C# name, stays classified, and silently reads back as its default. The new test binds a
document written from the catalog's names and compares against a **fresh instance** rather than
`default(T)`, because a string initialised to `string.Empty` is not null when it fails to bind.


### Fixed — a leaf that exits could report a fault and never clear it (`Journal` 1.8.0, `Lib` 4.35.0)

**Measured on the speech leaf.** It reported a model it could not load, exited when idle, woke with
the model fixed, and wrote **no recovery** — because the fresh process had never seen the fault. A
journal that reports a fault and can never clear it is worse than one that reports neither.

A process reports transitions from what it remembers, and a process that exits remembers nothing. The
journal is already the record, so `LeafState.DegradedComponents` reads it back and `LeafLifecycle`
takes it as a seed. Both directions then hold across a restart: a leaf that wakes healthy after
reporting a fault clears it, and one that wakes still broken says nothing — which is also what keeps a
socket-activated leaf from re-reporting the same condition on every wake.

- **`leaf_ready` wipes the slate**, which is the line separating the two kinds of leaf. A resident one
  writes it on every start, so everything before it described a run that has ended; a leaf that exits
  when idle writes none, which is exactly why its faults carry.
- **Only the newest segment is read.** A fault predating the segment boundary is not carried over —
  the alternative is opening older files on every start of a leaf that may start dozens of times a
  day, to recover a fault nothing has re-observed since midnight.
- **An unreadable journal seeds nothing.** A state that cannot be read is not evidence of a fault, and
  half a replay is worse than none: it would carry faults forward past the recovery that cleared them.
- **The seeded duration is measured from this process**, not from an invented earlier moment. How long
  a component was broken is only knowable by whoever watched it break; measuring from the restart
  understates it, which is honest.

### Documented — a self-re-execing leaf must supply its own start (`Journal` 1.7.1)

`LeafLifecycle` reads the process start from the OS by default, and an `execve` keeps the process id
— so a leaf that replaces its own image goes on being told when the *original* process began. Measured
on the watchdog's first hot-swap: a `StartupMs` of four hours. Not fabricated, and still the wrong
clock. The `startedAt` parameter is how such a leaf passes a moment captured at the top of its entry
point, which is also correct for a cold start.

### Added — a leaf reports its own state changes (`Journal` 1.7.0, `Lib` 4.34.0)

`TheKrystalShip.KGSM.Lifecycle` gives every leaf one way to say four things about itself:
`leaf_ready`, `leaf_degraded`, `leaf_recovered`, `leaf_stopping`. Nothing emits them yet.

**`LeafLifecycle` reports transitions, not states.** A leaf calls `MarkDegraded` from its polling loop
every tick and gets one line, because the change is decided from what this object has already reported
rather than from what the caller believes. A component already degraded is a no-op *even when the
detail differs* — a backend returning a different error string on each retry would otherwise turn one
outage into a stream. `MarkRecovered` for something that never broke writes nothing at all.

**The leaf writes no field names.** The payload is composed inside the emitter, so these four events
have exactly one writer however many leaves emit them, and the names live in `LeafLifecycleFields`
which the reader's payload classes bind to by `JsonPropertyName`. The equivalent drift is live
elsewhere in this ecosystem — a producer spelling payload names in its own repo against a reader class
in another, bound by nothing but case-insensitive matching — and it is what these events would have
multiplied by seven emitting repositories.

`Degraded` is a **component**, not a boolean: a leaf can be broken in two ways at once and recover from
one, and "the assistant's LLM backend is unreachable" is actionable where "the assistant is degraded"
is not. Keep the component set bounded — one built from a guild or a mount grows without limit.

Two things the design deliberately refuses:

- **There is no `leaf_stopped`.** The last thing a process can write is that it is stopping; whether
  it then stopped is not something it is around to say. A `leaf_ready` with no `leaf_stopping` before
  it *is* an unclean exit, and the journal is already the record that says so.
- **No payload names a leaf, and none carries a version.** The producer comes from the journal a line
  was read out of, which a reader can check, and `ProducerVersion` is already on every line. A copy in
  the payload would be a second answer able to disagree — which is why these do not derive from
  `ServiceEventData`, whose `Leaf` id is required and correct for kgsm-api's `service_*` events about
  *other* leaves.

`LeafStopReason.Idle` and `.Reload` are load-bearing on the consumer side: a socket-activated leaf
idling out is its resting state, and the watchdog's hot-swap keeps every supervised game running.
Neither is an outage, and both look like one without the reason.

### Added — one check reads every journal on a host and compares them (`Journal` 1.6.0, `Lib` 4.33.0)

`TheKrystalShip.KGSM.Conformance` reads what producers actually wrote and reports where it does not
match the envelope contract: thirteen rules over schema version, event-type spelling, payload shape,
timestamp precision, absent-spelling, actor, producer-version shape, unknown fields, journal
attribution, segment naming, readability, and the one that only exists across producers — that every
journal on a host names the same host.

**Producer-agnostic by construction.** Nothing in it knows which components exist; it is handed
journals and checks whatever it is handed. `HostJournalConformanceTests` hands it the same scan every
consumer uses, so a producer added later is covered the moment its journal exists, without anybody
remembering to cover it.

**Mechanism, never policy.** No rule looks at an event type or a payload field. What a producer
records, and when, stays its own business.

Two properties the check is careful about, because both are ways of measuring nothing and calling it
a pass:

- **An old line is allowed to look old.** A line records what the build that wrote it produced, so a
  journal's history legitimately holds shapes a current build would no longer write. A host check
  samples the newest lines — what the deployed builds are writing now — and the sample size is the
  caller's.
- **An empty scan fails.** A clean report over nothing looks exactly like a clean report over a
  host. A machine that is not a KGSM host sets `KGSM_CONFORMANCE_SKIP_HOST`; anything else fails
  rather than passing silently, and every report carries what it read (`kgsm-monitor(1)`) whether or
  not it found anything wrong.

`EventJournalWriter.TimestampFormat` is now a shared constant the writer formats with and the check
parses against, so the two cannot drift.

### Added — a journal no other account can reach says so (`Journal` 1.5.0, `Lib` 4.32.0)

`JournalAccess.DescribeUnreachable` checks whether a producer's **state directory** grants its group
access, and the writer reports it at construction. A directory cannot be entered without execute on
every directory above it, so a state directory closed to the group makes the journal inside it
unreachable however permissive the journal's own mode is.

**The result is silence, not an error.** A reader that cannot traverse in does not get a permission
failure it can report — `Directory.Exists` answers false, so discovery concludes the producer has no
journal, which is indistinguishable from a leaf that has recorded nothing. Nothing on the host tells
them apart, which is why the check runs where it does: the producer is the only party in a position
to notice.

Only the **group** bit is examined, deliberately. The ecosystem's answer to cross-account reads is
the shared `kgsm` group its units already name, not world access — a state directory holds more than
the journal (an API's session store, an assistant's conversation history), so "make it world-readable"
is not the remedy and is not suggested. `0750` and `0755`, the two modes every unit on this host
declares, are both silent.

### Added — a producer prunes the journal it owns (`Journal` 1.4.0, `Lib` 4.31.0)

`JournalRetention.Prune` removes segments past `EventJournalWriterOptions.RetentionDays`, defaulting
to **90 days** — the engine's own `event_journal_retention_days`, so a merged page's coverage is
bounded by one number rather than by whichever producer was least generous. Zero or negative keeps
everything, the explicit opt-out for a host that retains its trail elsewhere.

**Only one producer of five pruned anything before this.** The engine has a daily timer running
`kgsm events journal prune`, which prunes the engine's directory alone; every leaf journal grew
without bound. They are days old today, which is the point at which to fix it rather than the point
at which it hurts.

**Each producer prunes its own, and nothing else can.** A leaf's journal may be root-owned or sit
under a state directory another service user cannot enter — kgsm-firewall's is both — so a central
pruner would be a component reaching into directories it has no business in, and would have to be
granted the privilege to do it.

**The cadence comes from the data, not from a clock.** Pruning runs at construction and again when
the segment date rolls over, which is exactly daily for a resident daemon and is the smallest unit
retention can ever remove, since a segment *is* a day. So there is no timer, and with it no hosting
dependency — this package is consumed by a root-running firewall authority that builds no container.
Startup is what covers the other extreme: a socket-activated authority may exist for the length of
one request, and a timer would never fire in it. The gap left is a process that runs longer than
the window and records nothing in it — which is by construction a journal that is not growing, and
the next restart prunes it.

Two safety properties, both tested:

- **Whole segments are unlinked, never truncated.** Every consumer's position is a byte offset into a
  named segment, so rewriting one in place invalidates every cursor into it and silently misplaces
  every event after the cut. Removing the file whole makes a consumer report an `EventJournalGap` —
  a discontinuity it can say out loud.
- **Age comes from the segment's name, not its mtime.** A restore, a copy or a backup tool moves an
  mtime without any event moving. This differs from the engine's `find -mtime`; the two agree on a
  normally-operating host and only the name still agrees on a recovered one.

A file whose name is not a date is left alone rather than guessed at.

### Fixed — federation no longer depends on the order it was registered in (`Lib` 4.30.0)

`AddKgsmServices` and `AddKgsmJournalFederation` register the **same resolution rule** for
`IEventSource` and `IEventJournalHistory` — federated if a federated reader is in the container,
single-journal otherwise — so either call order produces the same result.

**The bug this removes had no symptom.** Two valid `AddSingleton` registrations of one interface
differ only in call order, so a consumer that federated too early kept reading its single journal
**successfully**: healthy journal, quiet host, nothing to catch, and the events it wanted sitting in
four other files. It cost kgsm-bot its announcements once already. Three of the four consumers carried
a comment warning about it; the two that no longer need one have had it removed.

A consumer with a genuine reason to supply its own source still can — an explicit registration
afterwards wins by last-registration, the same way it does for `IEventCursorStore`.

`JournalDiscovery.Discover()` now **scans once** and hands every caller that one answer. It was
called twice per registration, once for the history reader and once for the live tail: two scans are
two chances to disagree, and a journal appearing between them would leave one half of a consumer
permanently blind to a producer the other half reports on.

### Changed — `JournalRecorder.NormalizeType` is `protected` (`Journal` 1.3.0, `Lib` 4.29.0)

A recorder that names the event type in its own logging can spell it the way the journal does.
kgsm-watchdog's call sites name events the engine's command-line way (`instance-crashed`), so a
debug line logging the raw string and a journal line carrying the normalised one disagreed about
what had just been recorded.

### Added — one variable moves every journal on a machine (`Journal` 1.2.0, `Lib` 4.28.0)

`KGSM_JOURNAL_STATE_ROOT`, read by `AddKgsmJournal` (or passed as `stateRoot`), relocates the whole
layout — same producer name, same `events` subdirectory, a different root.

Deriving the journal path from the producer id is what keeps a writer and every reader agreed on it,
and it has a consequence worth naming: **a component run by hand writes exactly where the deployed
one does.** A leaf started from a checkout, or a test exercising one, appends to the host's real
audit record — and fabricated history is worse than none. Each producer had to solve this for itself
or not at all: `kgsm-api` has `Api__JournalStateRoot`, and until now nothing else had anything.

One variable rather than a per-leaf setting, because a process either writes to this host's journals
or to a throwaway root, and that is never a question about a particular leaf.

### Added — being a producer is one set of decisions, made once (`Journal` 1.1.0, `Lib` 4.27.0)

A component that writes its own journal decides four things: what to call itself, which directory to
append to, which version to stamp, and when that directory comes into existence. Each has a single
right answer a producer can derive, and the writer package now derives all four.

- **`JournalLayout`** composes a producer's journal directory and, in `ProducerOf`, inverts it —
  answering what a reader concludes from finding a journal at a given path. Writer and reader had
  been two implementations of one rule, which is a rule only for as long as they agree.
- **`EventJournalWriterOptions.DescribeDirectoryMismatch`** asks that question of a producer's own
  configuration, and `EventJournalWriter` reports the answer at construction. **A journal written
  where no reader scans has no failure to notice**: the writes succeed, and a producer whose
  directory is not found has honestly recorded nothing, so a misplaced journal and an idle leaf are
  the same observation. This is the moment anything can tell them apart.
- **`ProducerVersion`** resolves one build identity — the informational version, falling back to the
  assembly version, `null` when there is neither. `Resolve` exposes the precedence on its own so the
  rule is testable without an assembly to vary. **Never a fabricated fallback**: an omitted field
  claims nothing about a build, where `0.0.0` names one that was never made.
- **`JournalProducer.SystemActorFor`** derives the `system:<name>` actor an autonomous component
  attributes its own actions to. An actor held beside the producer id is a second spelling of one
  fact, free to disagree with it.
- **`JournalRecorder`** is the write path a producer records through: type normalisation, the actor
  and origin defaults, real JSON nulls rather than empty strings, and failure semantics that log what
  was lost and never throw — because the action a line describes has already happened, so refusing it
  over the record would trade a missing line for broken behaviour. A derived class writes its own
  event types and payload shapes, which are its vocabulary and belong to it.
- **`AddKgsmJournal(producer, versionSource)`** registers the writer and **creates the journal
  directory during startup**. A consumer discovers producers when it starts, so a producer whose
  directory appears on its first event is invisible until it emits *and* every consumer restarts.

`JournalDiscovery`'s state-root and subdirectory constants now come from `JournalLayout`, so where a
journal lives is one definition rather than a copy on each side of the split.

The package takes `Microsoft.Extensions.DependencyInjection.Abstractions` for the registration —
**Abstractions, and it stays that way.** The DI implementation would bring a container this package
never builds and Hosting a process model, into a root-running firewall authority whose attack surface
is why the writer lives outside the engine-interop library at all.

Additive throughout: no existing signature changed and nothing behaves differently until a producer
is migrated onto it. Authority: `event-conformance-plan.md`.

### Added — a console can be read past its tail

`GetConsoleWindowAsync` reads a window of one run's console and reports the byte range it came from.
Pass the `Start` it returns as the next call's `endOffset` and you get the lines immediately before
it, so a caller can page back to the beginning of a run of any size. **The cursor is a byte offset
because a line count from the end cannot do this**: the game prints between the two requests, so
"the 500 before the last 200" names a different line each time and consecutive pages silently
overlap or skip. `HasEarlier` is false once the run's start is reached. A daemon too old to report
the range answers the lines with no cursor, which reads as a window with nothing before it.

`OpenConsoleDownloadAsync` opens the whole of one run's log as a stream with its length — the file
somebody attaches to a bug report. A stream rather than a list because a log has no bound: nothing
between the daemon and where the bytes are going holds all of it. Null means there is no console to
serve, which stays distinguishable from a known instance that has never printed (an open download of
length 0).

`GetConsoleTailAsync` / `GetConsoleRunTailAsync` are unchanged and now read a window internally.

### Changed — package license metadata is GPL-3.0-or-later

`PackageLicenseExpression` now matches the repo's own `LICENSE` on every published package. Already
published versions keep the metadata they were built with, since a published version is immutable —
the correction reaches consumers on the next version bump.

### Added — the events a long run needs to report its outcome and its middle

Five more types, from an audit of every operation the engine brackets:

- **`instance_update_failed`** (`Fact`/`Failure`). An update that ends without the version moving had
  exactly two ways to look, and they were the same two lines: it succeeded with nothing to do, or it
  failed. Since a consumer settles an engine-driven run on its bracket, a refused update reported as
  a completed one. This is what tells them apart.
- **`instance_backup_started`/`_finished`** and **`instance_restore_started`/`_finished`** (`Phase`).
  The two backup verbs are minutes of archiving on a large world and a scheduler drives them
  unattended, so they now bracket their runs like the lifecycle verbs — a surface can show the
  instance as busy while it happens instead of learning at the end.

### Added — `instance_restart_stopped`, the middle of a restart

A restart runs its stop and its start through kgsm's own logic rather than the stop and start
commands, so nothing was emitted between `instance_restart_started` and `instance_restarted` at the
very end. For the whole shutdown — seconds to a minute, and the drain of a game that saves its world
— the process did not exist and every consumer still read the instance as running.

`InstanceRestartStoppedData` (`instance_restart_stopped`) is that middle: the old run is down, the
new one has not been spawned yet. Classified **`EventWeight.Phase`**, deliberately: it is a step
inside one operation, not a shutdown somebody asked for. Making it `instance_stopped` would put a
`server.stop` audit row and a "went offline" notification in the middle of every restart, on every
surface, which is the opposite of what a restart means.

### Changed — `AddKgsmJournalFederation` documents what happens when it is called too early

The ordering requirement was stated; the consequence of getting it wrong was not. Called **before**
`AddKgsmServices`, the federated registration is overwritten by the single-journal one and the call
does nothing — no exception, no log line, and a consumer tailing the engine alone while believing it
tails every producer, which reads as a quiet host rather than a misconfiguration. Both registrations
are valid; only the order decides which wins.

`namedJournals` also gains the `<param>` tag it was missing, which was the library's one build warning.

### Changed — the journal's write half is its own package

`IEventJournalWriter`, `EventJournalWriter`, `EventJournalWriterOptions` and `JournalProducer` moved to
**`TheKrystalShip.KGSM.Journal`**, whose whole dependency is `Logging.Abstractions`. This library depends
on it and keeps every reader, so **no consumer changes anything** — the namespaces are deliberately the
ones these types already lived under, and packing turns the project reference into a package dependency.

The reason is kgsm-firewall: it records the firewall edges it applies, and it runs as **root**. Taking
the whole engine-interop library to append one line would have put a process runner, an RCON client and a
set of HTTP clients inside a privileged helper. A producer that only records what it did now takes the
journal alone.

`IFirewallService.EnsureOpenAsync`/`RemoveAsync` gain optional `actor`/`origin`, forwarded on the wire
(Firewall.Contracts 1.2.0) so the authority can record who asked on the edge it performs. Provenance is
the caller's to state and the authority's to repeat — it cannot check the claim, so a caller that knows
nobody passes null rather than a stand-in.

### Added

- **A consumer can name a journal the scan would not find.** `JournalDiscovery` takes a `named` list
  (surfaced as `AddKgsmJournalFederation(namedJournals:)`), on the same footing as the engine's
  configurable directory. It closes a real hole for a consumer that keeps its OWN journal at a
  configured path: it would write a record and then be unable to read it back, because the scan only
  looks under each producer's default state directory. Named entries win over scanned ones, since a
  caller that says where a producer writes knows better than a directory that happens to share a name.
- **The Control Panel's own facts in the catalog.** Eighteen `kgsm-api` event types — `auth_login` /
  `auth_logout` / `auth_cluster_session` / `auth_session_revoked`, the six `user_*` account changes,
  `identity_linked` / `_unlinked`, the four `service_*` events, `file_written` and `backup_downloaded` —
  with their payload classes and descriptors, plus two new subjects, `EventSubject.Account` and
  `EventSubject.Service`. Both subjects are load-bearing rather than tidy: an unclassified type falls
  back to `Instance` by the engine's naming convention, which would have filed every sign-in on this
  host under whichever game server the reader happened to be looking at.
  - `Identity`, `Handle` and `UserAgent` are `Personal` — they link a panel account to a person
    somewhere else, or describe the machine they used. `Username` and `Tier` stay `Public`, because a
    trail that records authority changing and names nobody is not a safer log.
  - Each `service_*` event has its own payload rather than one class with mostly-null properties: a
    field an event can never carry still has to be classified for it, and the descriptor would end up
    describing a shape nothing writes.
- **An empty journal reads as "recorded nothing", not as unreadable.** A producer creates its journal
  directory up front so readers can discover it before its first event; reporting that as unreadable
  would undo exactly that, making a leaf that has simply not fired anything yet look like one nobody can
  read. A directory that is absent still reports unreadable — the two are different answers.
- **Host-scoped monitoring facts in the catalog.** `HostThresholdBreachedData` /
  `HostThresholdClearedData` and their `host_threshold_breached` / `host_threshold_cleared` descriptors,
  plus a new `EventSubject.Host`. A threshold episode may name the server it is about, but it is the
  **host's** monitoring that established it and most episodes name no server at all — so it gets a
  subject of its own rather than borrowing one it does not have.
  - **A breach and a recovery are two events, not one row that changes.** The journal is append-only;
    the mutable view of the same condition is the alert feed, which answers a different question.
  - The payloads carry **raw values only** — no summary sentence, no severity, no formatted number. A
    consumer renders those, and freezing one consumer's wording into the record would make every other
    consumer live with it.
  - `CloseReason` is load-bearing and must never be flattened into "recovered": an episode that ended
    because its rule was retuned, disabled or removed did **not** recover — the value was never observed
    to come down. `OpenedTs` travels on both events so a reader can place the breach without holding the
    pair.

- **Event journal federation — the writer, the multi-journal reader, and the v1 envelope.** A
  component records what it did in its own journal instead of asking another component to write it
  down. Every addition is an overload or an optional field beside what exists, so nothing that reads
  the engine's journal today changes behaviour or id values (authority:
  `../event-journal-federation-plan.md` §2, Phase 1).
  - `IEventJournalWriter.AppendAsync(eventType, Action<Utf8JsonWriter>, …)` — the overload a producer
    actually wants, since a component holds typed values rather than a `JsonElement`. The two other ways
    of bridging that are both worse: composing JSON by string concatenation puts an escaping bug one
    unusual instance name away, and serializing a payload model needs a registered type per event in a
    library that must stay reflection-free. A default interface implementation, so nothing else has to
    implement it.
  - `IEventJournalWriter` / `EventJournalWriter` — appends one whole v1 line per event via a single
    `O_APPEND` write (atomic below the 4096-byte `PIPE_BUF` limit, logged past it). The envelope is
    composed with `Utf8JsonWriter` rather than serialized from a model, which fixes field order,
    omits nulls instead of writing them, and adds no registered type. Configured with
    `EventJournalWriterOptions` (producer, directory defaulting to `/var/lib/<producer>/events`,
    version, hostname). Takes no producer parameter per call: a producer id that could be supplied
    per event would be a claim about authorship rather than a fact about the writer.
  - `FederatedEventJournalHistory` — merges every producer's journal into one page, timestamp
    descending with the event id as tie-break. Aggregation lives here rather than in a consumer, so
    each surface aggregates natively and the one serving it over HTTP is exposing the merge rather
    than being it. A journal absent from the source list is not read; one that is listed but
    unreadable is reported per-producer instead of silently contributing nothing.
  - `JournalCoverage` on `EventHistoryPage.Journals` — per-producer coverage, readability and
    truncation. The collapsed `CoverageFrom` is the **newest** readable floor: past that point some
    producer's retention has already dropped what it held, and reporting the oldest would present a
    partial window as full coverage.
  - `AuditId.ForPosition(producer, segment, offset)` + `TryParseProducerPosition` — producer-first
    (`evt_watchdog_2026-08-07_000000001234`) so plain string comparison still orders by
    `(producer, segment, offset)`, the cross-journal tie-break within one timestamp. Parsing fails
    closed on an unprefixed or unconventionally-named id: a caller asking who wrote an event gets
    "this id does not say" rather than a guess.
  - `JournalProducer` — the producer-id format rule (lowercase, digits, dashes, no underscore, which
    is what keeps a position id readable) and the one producer the library knows by name. Deliberately
    not a registry of leaves: which journals a host has is discovered from the host, and a list here
    would be a second answer able to disagree with it.
  - `EventWrapper.SchemaVersion` (`V`), `ProducerVersion`, and `EmittingVersion` — schema version and
    producer build kept separate, because one says how to read the line and the other says which build
    wrote it. `EmittingVersion` falls back to the v0 `KGSMVersion`, so a line written before these
    fields existed reads without a migration for as long as retention holds it.
  - `EventWrapper.OpId` / `RunId` / `During` and their `EventHistoryEntry` counterparts — **reserved
    for correlation and populated by nothing.** Declared now so correlation costs no second envelope
    change. A producer may stamp an `OpId` it was **given** or **minted**, never one it
    **inferred**; an observed coincidence belongs in `During`. `OpId` asserts causality and `During`
    asserts co-incidence, and the separation is part of the contract rather than of the later work.
  - `EventHistoryEntry.Producer` — stamped by the reader from the journal a line was read from, never
    read out of the payload. A field inside the data is a claim a reader cannot check; where it came
    from is one it established itself.
  - `EventPosition.Producer` — null when the transport did not say, rather than defaulting to `kgsm`:
    a transport that reports no producer has not told us it was the engine, and `default(EventPosition)`
    skips property initializers so any non-null default would be a lie in the default value.
  - `IFederatedEventCursorStore` / `FileFederatedEventCursorStore` — one cursor per producer in one
    file, so a consumer reading N journals advances each independently and a leaf that was down catches
    up without replaying or skipping any other's.
  - `FederatedEventSource` — the live counterpart to the federated history: tails every producer's
    journal at once and delivers all of them as one stream. It **composes** one `EventJournalReader`
    per producer rather than reimplementing the tail, because whole-line framing, segment rolling,
    straggler grace and gap detection are solved once already and a second implementation of them
    would be a second set of bugs. Every delivered `EventPosition` carries the producer, stamped from
    the reader that produced it. `GapDetected` names which producer's history is incomplete, so a
    consumer can qualify one journal's coverage without implying anything about the others. A journal
    whose directory does not exist yet is picked up when it appears, so a leaf installed later is
    tailed without a restart.
  - `IJournalDiscovery` / `JournalDiscovery` — which journals a host has, found by **looking for the
    ones that exist**. Every producer writes to `<its state directory>/events`, so a directory at
    `/var/lib/kgsm-watchdog/events` *is* the watchdog's journal and its producer id is `kgsm-watchdog` —
    the same name the writer used to choose that path. The directory is ground truth rather than a second
    answer able to disagree with the writer. The engine falls out of the same rule
    (`/var/lib/kgsm/events` → `kgsm`) and is also added explicitly, since its journal location is
    configurable.
  - **A journal is never located by deriving a path from a name.** Measured on a live host: one leaf's
    unit and state directory differ (`kgsm-assistant-service` vs `kgsm-assistant`), and two leaves have no
    `StateDirectory` at all — so any name-based convention is right for most and silently wrong for the
    rest. Wrong is expensive both ways: the writer cannot create a directory under root-owned `/var/lib`
    so the event is lost, while a reader looks in the same empty place and calls the producer unreadable
    forever.
  - A producer that has written no event has no journal directory and is simply absent from discovery —
    the honest answer, since there is nothing to read and listing it would report a leaf's silence as a
    failure to read it. The scan is narrowed to this ecosystem's own state directories, so an unrelated
    service that keeps an `events/` directory is never mistaken for a producer. Producers are ordered by
    name so the same-timestamp tie-break is identical on every host and restart; directory enumeration
    order is not guaranteed, and an order that varied per process would make two readers of one record
    disagree about which of two simultaneous events came first.
  - `AddKgsmJournalFederation(...)` — opt-in registration that replaces `AddKgsmServices`'
    `IEventJournalHistory` and `IEventSource` with the federated pair by last-registration, so every
    handler a consumer already registers keeps working: `EventService` resolves `IEventSource` and
    never learns what backs it. The federated source keeps one cursor **per producer**, a different
    store from the single-journal `IEventCursorStore` and not migratable from it — a position in the
    engine's journal says nothing about a position in anyone else's — so a consumer switching over
    starts each journal from the given `EventStartPosition`.

- **`WatchdogConsoleRun.Outcome` + `ExitCode`** — how the supervisor classified each run's ending
  (`crashed` / `gave-up` / `exited` / `stopped` / `running` / `unknown`), and the exit code where one
  could be read. Matching a crash on `EndedAt` alone can only ask which run stopped printing nearest
  it; this says which run the supervisor itself watched fail, and tells a crash apart from a
  deliberate stop — a distinction no amount of timestamp comparison recovers. A daemon that predates
  the run ledger sends no field, which binds to `"unknown"`: an absence of knowledge, never a clean
  ending.

- **`IWatchdogClient.GetConsoleRunsAsync` + `GetConsoleRunTailAsync`** — a console's runs, and one
  run's output. The supervisor rotates an instance's log on every fresh spawn, so a crash and the
  restart behind it are two runs: reading the live console after a crash-restart shows a clean boot
  and nothing of what went wrong. A consumer diagnosing a crash lists the runs, matches it against
  `WatchdogConsoleRun.EndedAt`, and reads that index.

  `EndedAt` is null only while a run is `Current` — meaning a process is alive in the instance's
  cgroup writing it. A stopped instance has no current run, including the one still sitting at the
  live path awaiting the next spawn's rotation, so a crash with nothing restarting behind it is
  still findable by its end time.

  The index is positional and newest-first, so it is only meaningful against the listing it came
  from — read, pick, use, rather than store. No file path is exposed; the bytes come back through
  the client. `GetConsoleTailAsync` keeps its meaning exactly (run 0, the most recent) and now
  delegates. A daemon too old to serve the route answers 404, which reads as no runs.

- **`KgsmEventCatalog`** — the one registry of what the engine emits, so every consumer stops
  working it out separately. Per type: the class its payload deserializes into (`PayloadType`), its
  subject, whether it reports a fact or a step inside one (`EventWeight`), whether it reports
  something completing or failing (`EventOutcome`), and per payload field what kind of data that
  field holds (`FieldSensitivity`, `FieldShape`). All 55 typed events are classified.

  **`EventService` dispatches off it**, so an event that can be deserialized is necessarily one that
  has been classified — there is no second table to fall out of step with, and an event is added by
  adding a descriptor because there is nowhere else to register it. The `Instance<TData>` /
  `BlueprintEvent<TData>` helpers are constrained to the matching payload base, so an event's subject
  and its payload's own base cannot disagree either.

  **It states facts and never policy.** `Phase` does not mean "hide this" and `Personal` does not
  mean "refuse this" — a consumer decides what to do with a fact, and two consumers may decide
  differently. The moment the catalog carries a permission, every surface inherits whichever one
  wrote the rule.

  `FieldSensitivity` is why: `PlayerAddr` is `Personal` (it identifies a person rather than a
  player, and the game shows it to nobody), `Command` is `Privileged` (a console command can create
  an operator or carry a token), and a moderation `Target` is `Conditional` — it may be an address,
  a name or an id, the blueprint declares which, and the event does not carry that. This library
  already refuses to classify a `Target` on a consumer's behalf; the catalog refuses for the same
  reason rather than guessing.

  `Describe` never returns null. An unrecognised type comes back with `Known` false, its subject
  read off the engine's naming convention, and **no fields** — which means "render nothing from the
  payload", not "the payload is empty". An event nobody has classified may carry anything.

  Three tests hold it to the engine: every declared payload property is classified, no descriptor
  names a field its payload does not have, and every event data class in the assembly is named by
  some descriptor — that last one catching a payload class added and never wired, which the runtime
  would drop as an unknown type. A new event or a new field fails this build until somebody
  classifies it. The library stays reflection-free at runtime; the reflection is in the test project.

- **`IInstanceFiles.Find` and `IInstanceFiles.Search`** — a recursive name walk (glob) and a content
  search (regex) inside an instance's jail, so a consumer can locate a game's own config without
  descending a directory at a time. A game's config layout is a fact about the game, not about KGSM's
  paths, so it is found rather than known: reaching Palworld's `PalWorldSettings.ini` cost five
  sequential single-level listings before this.
- **Symlinked directories are recorded and never descended into.** Containment for the walk is that
  absence of a path out, not a check applied afterwards — the weaker of two containment rules is the
  one an attacker picks, so there is only the one.
- Both bound the work (`FindOptions` / `FileSearchOptions`) and report **truncation separately from an
  incomplete walk**: "more matched than were returned" and "the walk stopped early" are different
  facts, and collapsing them lets *I stopped looking* read as *that is all there is*. Directories
  named `backups` are skipped unless asked for — an archived copy is not the file a question about the
  live server is about. Content search skips binaries and oversized files, and refuses an expression
  that does not compile (`FileOpOutcome.InvalidArgument`); patterns run `NonBacktracking`, since a
  caller-supplied regex over thousands of files is otherwise a denial of service.

### Changed

- **`IWatchdogClient.GetAllPlayersAsync` → `GetPlayerPresenceAsync`, and it now answers whether a
  roster is knowable at all.** It returns
  `IReadOnlyDictionary<string, WatchdogInstancePresence>` — every instance the supervisor knows,
  each carrying `Detection` (`log` / `rcon` / `none` / `unknown`) beside its sessions.

  The old shape listed only instances with tracked sessions, which made an absent instance ambiguous
  between "nobody is online" and "this game cannot report players" — and every consumer that read the
  first meaning of the second stated something the host does not know. Detection travels with the
  roster so a caller cannot take one without the other; `WatchdogInstancePresence.IsDetected` is the
  guard, true only for `log` and `rcon`.

  **The supervisor decides it.** The predicate includes whether a pattern *compiles*, which is not
  something a consumer can re-derive from an instance's config — and three surfaces each deriving it
  is how they come to disagree. Renamed rather than overloaded so the shape change is a compile error
  at every call site.

- **`GetPlayerPresenceAsync` returns null on an unreachable daemon** instead of throwing
  `HttpRequestException`, matching what the contract already documented and what every other read on
  this client does. Null is the daemon being unavailable, never a host with nobody playing.

### Fixed

- **`RconClient` authenticates with `SERVERDATA_AUTH`.** The auth packet is type 3; type 0 is
  `SERVERDATA_RESPONSE_VALUE`, a server→client type. A server answers it with id -1 — the same
  rejection a wrong password earns — so the failure reads as a credentials problem and survives any
  amount of checking the password.

- **The auth verdict is matched by packet type, not by arrival order.** Servers may precede
  `SERVERDATA_AUTH_RESPONSE` with an empty `SERVERDATA_RESPONSE_VALUE` carrying the same id. Taking
  the first packet as the verdict leaves the real one queued and every later read returns the
  previous request's packet.

- **A command response ends at the protocol's sentinel.** `ExecuteCommandAsync` follows the command
  with an empty `SERVERDATA_RESPONSE_VALUE` and reads until the server echoes it, which orders
  correctly behind however many packets the response occupies. Waiting instead for an empty body
  never returns for a server whose reply is a single packet with content and no trailer.

- **Split responses are concatenated verbatim.** The parts are byte continuations; the newline
  previously inserted between them landed inside whatever token straddled the split.

- **Reads have a deadline.** `NetworkStream.ReadTimeout` governs only synchronous reads, so an async
  read carried no timeout of its own and a server that stopped answering hung the call for as long as
  the caller's token allowed. Exhausting it now raises `RconException`.

- **A 1–2 character response body is no longer dropped.** The body was read only when the packet
  exceeded 12 bytes, two more than the 10 an empty body occupies.

### Added

- **`Instance.RconPlayersRegex`** — the blueprint's pattern for reading one player out of
  `RconPlayersCommand`'s output, with optional named groups `id` and `name`. Rosters are worded per
  game — a header and one `-Name` line per player here, an id and a name in columns there — so the
  shape travels as data and a consumer applies it without knowing which game it is polling. Empty
  means the output cannot be read, which is not the same as a server reporting nobody connected.

- **`InstanceUpdateAvailableData`** — the engine's `instance_update_available` event, carrying
  `CurrentVersion` and `LatestVersion`. Update availability is a fact kgsm establishes and announces
  through the journal, so every consumer reads it the same way it reads the rest of `server.*`;
  nothing has to run its own probe or keep the answer in memory.

- **`IInstanceService.CheckUpdate(instanceName, emit, actor, origin)`** — `emit` runs the engine's
  recording check, which writes what it found beside the instance and announces a version it has not
  announced before. Recording is what makes a repeated sweep silent, so exactly one caller per host
  owns it; left `false`, the check answers the caller and changes nothing. `actor`/`origin` stamp the
  announcement the same way every other mutating verb stamps its events — an unattended sweep that
  omits them is attributed to whatever OS user it runs as, which reads as a person having asked.

- **`VersionInfo.CheckedAt`** — when `Latest` was fetched from upstream. A fast status read answers
  from the record the engine keeps, so how old the answer is comes with it. `null` means no check has
  ever run, never a substituted moment.

- **`KgsmTimeoutOptions.UpdateCheck`** (3 minutes) for the two commands that reach a game's upstream —
  `check-update` and `version --latest`. Both sat on the 30-second default meant for local reads,
  which is the same ceiling a container's registry probe already uses internally: the outer timeout
  would kill the process tree and report a timeout for a check the engine was about to answer
  honestly.

- **`IInstanceBackups.OpenArchive(instance, backupId)`** — read access to a compressed backup's archive
  bytes, plus the sha256 its manifest recorded. Its own service rather than a second root on
  `IInstanceFiles`, because backups deliberately live OUTSIDE the working directory (uninstalling an
  instance removes that directory wholesale and leaves the backups intact) — two roots with different
  lifetimes, and folding them together would make "the instance's files" mean two things.

  Only a **compressed** backup can be opened. An uncompressed one is a `data/` tree, not a single
  artifact: there is nothing to hand over as one stream and no digest to verify it against, so it is
  refused rather than tarred on the fly into something the manifest never described. The digest is the
  manifest's, carried verbatim — recomputing it from the bytes being served would read the archive twice
  and prove nothing about their provenance.

  The **manifest is the gate**: a directory with no readable manifest is not a backup, which is what
  keeps a half-built one (still staging) and a foreign directory invisible — the same rule the engine's
  own listing applies, so the two cannot disagree about what exists.

### Changed

- **The path-containment jail is one implementation** (`InstanceJail`), shared by `InstanceFiles` and
  `InstanceBackups` rather than written twice. Two jails that are "the same rules, written twice" drift,
  and a drifted containment check is not a weaker guarantee but no guarantee — the weaker of the two is
  the one an attacker picks. The roots differ per service; the rule does not.

- **`IInstanceService.DeleteBackup(instance, backupId, actor, origin)`** — remove one backup by id.
  The engine accepts only an id it itself lists as a backup, so a directory in the backups store
  carrying no manifest is refused rather than removed.

- **`InstanceBackupDeletedData` and `InstanceBackupsPrunedData`** — the two backup-removal events,
  registered in the type map and `KgsmJsonContext`. Separate types because they answer different
  questions: a delete is an operator naming one snapshot, a prune is retention policy sweeping
  whatever fell outside the keep window. The delete carries the backup id in `Source`; the prune
  carries `Deleted`/`Kept` as ints, since one event covers the whole sweep and the ids it removed are
  exactly the ones the instance no longer lists.

### Removed

- **`IWatchdogClient.OpenUpnpAsync` / `CloseUpnpAsync`**, and the `WatchdogUpnpActionResult` /
  `WatchdogUpnpOpenRequest` models they carried. An instance's router forwards are opened by the
  supervisor when it starts and released when it stops, so there is no on-demand open for a client to
  drive; the watchdog no longer serves the routes these called. `GetUpnpAsync` stays — reading what the
  router actually holds is a question a diagnosing operator has. **Breaking**, hence the major bump: an
  implementer of the interface loses two members.

### Added

- **`InstanceUpnpReassertedData`** — the typed half of kgsm's `instance_upnp_reasserted`
  (kgsm 3.11.0-rc1), registered in the event-type map and in `KgsmJsonContext`. A router forward that
  went missing while its instance kept running, put back by the watchdog's sweep. Separate from
  `InstanceUpnpOpenedData` because an open accompanies a bring-up while this one says the mapping
  disappeared with nothing on this host asking for it. `Ports` holds the subset that was missing, not
  the instance's whole configured set.

- **`InstanceRestartStartedData` / `InstanceRestartFinishedData`** — the typed halves of kgsm's restart
  bracket (`instance_restart_started` / `instance_restart_finished`, kgsm 3.7.4-rc1), registered in the
  event-type map and in `KgsmJsonContext`. A restart runs its stop and its start internally, so no
  other event fires between them: this pair is the only thing that spans the run, and it completes the
  set — update, stop and restart are all bracketed the same way now. Finished says the run ended;
  `InstanceRestartedData` remains the separate fact that the instance came back.

### Added — `IEventJournalHistory`, reading the journal back

Querying what the engine did, as the companion to `IEventJournalReader`'s tailing of it. Both read
the same segments, and neither needs anything running besides the engine that wrote them — so a
host answers for its own history with no daemon, no index, and no leaf installed.

```csharp
EventHistoryPage page = await history.QueryAsync(new EventHistoryQuery
{
    Instance = "factorio", SinceMs = since, Limit = 50
});
```

Filters are `Instance`, `Blueprint`, `Type`, `SinceMs`, `UntilMs`, ANDed, each optional. `Instance`
and `Blueprint` are orthogonal: a server and the blueprint it was built from routinely share a name,
and a query for one never returns the other. Results are newest-first, keyset-paged through
`BeforeTsMs`/`BeforeId` → `NextCursorTsMs`/`NextCursorId`, and `Limit` is clamped to `[1, 1000]`.

The cursor is a `(timestamp, id)` pair, not an id alone, so a caller merging this history with
another source can page both feeds from one cursor. The id it passes may belong to that other source
and name no event here — it is only ever compared as a tie-break, never resolved against the
journal. Resolving it would mean an id from the other feed read as "no cursor", and the caller would
page the newest rows forever.

There is no index and no cache, which is what stops a second copy from disagreeing with the record.
Segments are named by date, so a bounded window is narrowed by file *name* before one is opened;
segments are read newest-first and the scan stops when the page fills; and each is streamed forward
once with matches held in a ring buffer the size of the page, so memory is bounded by the page and
no file is read backwards or loaded whole. `KgsmOptions.EventHistoryScanBudgetBytes` (64 MiB) bounds
the read regardless, and hitting it sets `Truncated` rather than passing off a prefix as the whole
answer.

Three signals keep a partial answer from reading as a complete one. `CoverageFrom` is the oldest
moment the journal can still answer for — retention deletes whole segments oldest-first, so it is
exact. `JournalReadable` distinguishes an unreadable journal from one that matched nothing.
`Truncated` reports a budgeted scan. A query never throws for a missing or unreadable journal; an
event carrying no timestamp is reported and dropped rather than given a fabricated one.

### Changed — events carry their journal position (**breaking**)

`IEventService.RegisterRawHandler` takes `Func<EventWrapper, EventPosition, Task>`, and
`IEventSource.EventReceived` takes `Func<string, EventPosition, Task>`.

`EventPosition` is the segment and the byte offset an event's line begins at. Because each event is
one whole line and retention deletes whole segments rather than rewriting them, no two events share
a position and an event's position never changes — so `AuditId.ForPosition` turns it into a stable
id. That is what lets a consumer watching events arrive and a consumer reading history back name the
same event identically with no coordination: one takes the position from the transport, the other
from the file, and both compute the same id.

Only a raw handler receives it; typed dispatch does not, so a consumer needing the id inside a typed
handler captures it from a raw handler first.

### Added — `AuditId.ForPosition` / `TryParsePosition`

`evt_<segment>_<offset>`, e.g. `evt_2026-08-07_000000001234`. Unique by construction, and ordered
like the file — segment names are equal-width dates and the offset is fixed-width, so comparing two
ids as plain strings compares their positions in the journal. One value therefore serves as both
identity and pagination cursor.

`AuditId.ForEvent` remains, for a caller holding an envelope with no position to hand. It hashes a
timestamp of one-second granularity and so cannot distinguish two identical events emitted within
the same second; `ForPosition` can, and is what the audit trail is keyed on.

### Added

- **`InstanceStopStartedData` / `InstanceStopFinishedData`** — the typed halves of kgsm's shutdown
  bracket (`instance_stop_started` / `instance_stop_finished`, kgsm 3.7.3-rc1), registered in the
  event-type map and in `KgsmJsonContext` so an AOT consumer can deserialize them. They give `stop`
  the shape `update` already has: a consumer learns that an instance is shutting down for as long as
  the supervisor waits for the game to drain, from the journal alone, whichever entrypoint drove it.
  Finished is emitted on every outcome — it says the run ended, while `InstanceStoppedData` remains
  the separate fact that the instance is down.

### Added — player moderation

- **`IInstanceService.Kick` / `Ban` / `Unban`**, each taking the instance, the target, and the
  optional `actor` / `origin` provenance pair the rest of the operational verbs take. They run
  `kgsm instances kick|ban|unban <instance> <target>` and **pass the target through untouched** —
  the engine substitutes it into the blueprint's template, so the lib never builds the console
  command itself. A second implementation of that substitution here would be a second answer that
  could disagree with the one that actually runs. A target containing a line break is rejected
  before a process is spawned (a console reads one command per line, so it would deliver a second
  command nobody issued).

- **`ModerationCommand.TryGetTargetKind` and the `ModerationTargetKind` enum (`Ip` / `Name` /
  `Id`)** — the identity contract, read out of the template's placeholder. A blueprint writing
  `kick {ip}` says both "the verb is kick" and "hand it an IP address", so a caller reads the
  placeholder to know which field of a player record to send. A template with no recognised
  placeholder, or with more than one, is reported as **unsupported** rather than resolved to a
  guess — an ambiguous template names no single identity, and a bare verb would send the command
  with no target at all.

- **`Blueprint.KickCommand` / `BanCommand` / `UnbanCommand`** (nullable) and the matching
  `Instance` properties bound from the `kick_command` / `ban_command` / `unban_command` wire
  fields. Empty/null means the game declares no such command, in which case the engine refuses the
  action rather than approximating it with a different one.

- **Three moderation event types** — `instance_player_kicked`, `instance_player_banned`,
  `instance_player_unbanned` (`InstancePlayerKickedData` / `BannedData` / `UnbannedData`, sharing
  `InstanceModerationDataBase`), carrying `Target` and the resolved `Command`. They are their own
  types rather than `instance_input_sent` records because the subject is a player, not a command:
  a consumer asking "who was banned on this server" filters on the type instead of pattern-matching
  text a hand-typed `SendInput` could also produce. Unlike the join/leave pair (autonomous
  observations stamped `system`), these carry operator provenance.

  `Target` is carried **verbatim and never classified** — the blueprint is where that meaning is
  declared, and re-deriving it here would be a second answer that could disagree. A consumer that
  needs the kind reads it from the instance's template with `ModerationCommand.TryGetTargetKind`.

  Note for a consumer offering "lift a ban": an unban's subject is by definition not connected, so
  it cannot be resolved from a live player roster — `instance_player_banned` is the record to
  select from.

  Requires kgsm ≥ 3.7.0-rc1.

### Removed — BREAKING: the Unix socket event transport

- **`UnixSocketClient` and `IUnixSocketClient` are gone**, along with `KgsmEventTransport`,
  `KgsmOptions.SocketPath`, `KgsmOptions.EventTransport`, the
  `AddKgsmServices(services, kgsmPath, socketPath)` overload, and
  `IEventManagementService`'s `EnableSocket` / `DisableSocket` / `TestSocket` / `GetSocketStatus`.
  The engine no longer has a socket transport to drive. **This is the major version bump.**

  Socket binding is exclusive — one socket, one reader — which is the whole reason a consumer
  ever needed its own path and the engine ever needed to be configured with the list of them.
  The journal is a plain file: every consumer on a host reads the same directory, concurrently,
  with no registration and nothing to reserve. There is no longer a transport to select, so
  `AddKgsmServices(kgsmPath)` and the options overload both wire the journal reader.

  **Migrating:** drop the `socketPath` argument (or `SocketPath` / `EventTransport` from your
  options) and set `EventJournalDirectory` if the engine's journal is not at
  `/var/lib/kgsm/events`. Handler code is unaffected — it was already written against
  `IEventService`, which is what made this swap possible without touching a consumer.

  `SocketException` stays: it belongs to `IFirewallService`, which talks to the kgsm-firewall
  authority over its own socket and is unrelated to events.

### Fixed
- **`EventService.Initialize()` is idempotent.** Two callers legitimately reach it — `KgsmClient`'s
  constructor and whatever the consumer wires — and a second pass both re-subscribed the transport's
  `EventReceived` and started a second read loop, so one event was delivered **four** times
  (twice-subscribed × two loops). Every consumer then did its thing four times over: four Discord
  announcements, four notifications, four cache busts for one server starting. On the journal
  transport the two loops additionally raced the reader's single cursor. Repeat calls now log at
  debug and return; a caller must not have to know who else initializes.

### Added
- **The event journal transport.** `EventJournalReader` tails the engine's append-only NDJSON
  journal (`/var/lib/kgsm/events/YYYY-MM-DD.ndjson`) instead of binding a socket, selected with
  `KgsmOptions.EventTransport = KgsmEventTransport.Journal` or the new
  `AddKgsmServices(kgsmPath)` overload. Both transports now sit behind `IEventSource`, which
  `EventService` consumes, so **no handler code changes** when a consumer switches — and each
  consumer switches on its own schedule. The socket stays the default: taking this version does
  not move a consumer's transport on its own.

  What the journal buys is what a socket cannot do. Binding is exclusive, so each consumer
  needed its own path and the engine had to be configured with the list of them; a file has no
  such constraint, so any number of consumers read the same journal and the engine holds no
  knowledge of who reads. Delivery stops being live-only: a consumer that was down catches up
  from its stored position (`IEventCursorStore`, with `FileEventCursorStore` and
  `NullEventCursorStore` supplied, and a consumer that owns a database expected to store the
  cursor there). Position is a segment plus a byte offset, which is exact because each event is
  one whole line — only complete lines are dispatched, so a partially-flushed append is picked
  up whole on the next pass rather than delivered truncated.

  Where a consumer starts is an explicit per-consumer decision (`EventStartPosition`): a
  consumer that materializes the journal into an index must be able to replay it, while one
  that announces events must never replay a backlog. When a stored position can no longer be
  satisfied — retention deletes segments on age alone and never consults a consumer — the
  reader reports an `EventJournalGap` through `IEventService.RegisterGapHandler` and then falls
  back to its cold-start position, so a consumer can record that its history before that point
  is incomplete instead of presenting a partial record as a whole one. The socket transport
  could not express this at all: a missed event was indistinguishable from one that never
  happened.

  Delivery is at-least-once — the cursor is stored only past events already dispatched, so a
  crash costs re-delivery rather than loss, and a consumer that persists what it reads must be
  idempotent (a deterministic `AuditId` is what makes that free). The saved position is the
  oldest unfinished one, which also covers an emit that starts just before midnight and lands
  in yesterday's segment after the reader has moved into today's.

### Changed
- **The scheduled backup cadence replaces the on-restart backup toggle.** `Instance` carries
  `BackupSchedule` / `BackupTime` / `BackupDay` (kgsm's `backup_schedule`, `backup_time`,
  `backup_day`), and `AutoBackupOnRestart` is gone. A backup is taken against the instance as it
  is, running or not, so it no longer needs a restart window to happen in and the two schedules
  are independent. `Timezone` now serves both — one instance has one answer for what time it is.

### Added
- **Server notes** — `IInstanceService.SetInstanceNote()` plus `Instance.NoteBody` /
  `NoteUpdatedBy` / `NoteUpdatedAt`: the operator-authored free-text note a surface renders on a
  game server. Stored in the instance's `.config.ini` under `note` (base64) + `note_updated_by` +
  `note_updated_at`, so kgsm itself needs no note-specific command — the write is three ordinary
  `config-set` calls and the read comes off the roster. `InstanceNote` owns the codec: the body is
  encoded because that file is sourced as `key="value"` and re-emitted through a tab-delimited jq
  pipeline, where a raw quote, `$`, backtick, tab or newline would brick the instance. Attribution
  is written first and the body last, and `InstanceNoteResult` reports which keys landed, so a
  partial write can never credit a new body to the wrong person. A value that does not decode is
  returned verbatim, so a hand-edited note still renders. Bodies are capped at 600 characters and
  an over-long one throws before any write rather than being truncated.

### Added
- **`IInstanceService.GetBackupsDetailed()`** and the `InstanceBackup` model — an instance's
  backups with everything each one records (id, creation time, captured version, size, file
  count, sources, sha256), newest first, from `kgsm instances backups <instance> --json`.
  `GetBackups()` still returns the id-only listing for callers that need to distinguish an
  engine failure from an empty store. `InstanceBackup` is registered in `KgsmJsonContext`
  (required — there is no reflection fallback under AOT). `Sha256` is null for an
  uncompressed backup, which is a tree rather than a single artifact; null means "not
  applicable", never a placeholder digest.

### Added
- **`IBlueprintService.GetScaffold()`** — reads the engine's blueprint skeleton
  (`<KGSM_TEMPLATES_DIR>/blueprint.tp`) verbatim, so a surface seeding a new blueprint's buffer starts
  from the same authoritative template the assistant's authoring lane works from. The directory is
  engine-reported (`kgsm --paths --json`), never re-derived in C#; a missing directory or unreadable
  file returns `null` rather than a substitute skeleton. The template's instructional header is part of
  the returned text — it is the authoring help a manual writer reads while filling the file in.

### Added
- **RCON support**: `IRconClient` interface, `RconClient` (minimal Source RCON protocol
  implementation), `RconException`. AOT-safe, no external dependencies. Connects via TCP,
  authenticates with a password, executes commands, and returns raw text responses. Intended
  for periodic polling (connect → execute → disconnect) rather than persistent connections.
- **RCON properties on `Instance` model**: `RconPort` (nullable int), `RconPassword` (string),
  `RconPollIntervalSeconds` (nullable int), `RconPlayersCommand` (string, default "players").
  Materialized from the blueprint into the instance config; the watchdog reads these to poll
  game servers for connected players.

### Added
- **`FileOpResult<T>.Errors`** — the individual reasons behind a multi-reason failure, kept as a list so a
  surface can render one line per reason instead of splitting `Message` back apart. `WriteRaw`'s
  `InvalidDraft` is the one producer today, carrying the engine validator's own error strings verbatim;
  every other outcome leaves it empty and says everything in `Message`.
- **`IBlueprintFiles.ReadRaw(name, maxBytes)` + `WriteRaw(name, content, opts)` — byte-level blueprint
  file I/O.** The typed path (`Create`/`Render`/`TryParse`) handles native blueprints only and strips
  every comment, so a container blueprint or a commented one cannot survive a round-trip through it.
  These read and write the exact bytes instead, which is what lets a surface edit any blueprint as text.
  `ReadRaw` is the one method whose jail spans BOTH engine-reported blueprints directories — a shipped
  blueprint has to be readable to be edited into an override — while every write stays user-dir-only:
  saving an edit to a shipped blueprint creates an override that shadows it, and the system directory is
  structurally unreachable. `WriteRaw` validates through the ENGINE on a temp file the engine's
  `*.bp.yaml` glob cannot see, so an invalid draft never occupies the real filename; a rejection returns
  `InvalidDraft` carrying the engine's full error list. `BlueprintWriteOptions` carries `ExpectedEtag`
  (guarding the file that was READ, which for a first override is the system file), `MaxBytes`, and the
  `Actor`/`Origin` stamped on the emitted event.
- **`IBlueprintService.FindAll(name)` + `Validate(nameOrPath)`** — the engine's path-resolution and
  schema-check surfaces, typed. `FindAll` reports every candidate path with whether it exists, which is
  how a consumer tells a purely custom blueprint apart from a user copy shadowing a shipped one; unlike
  `FindPath` it reports on existence alone, so a MALFORMED blueprint stays locatable — precisely the file
  an editor is opened to repair. `Validate` probes rather than executes, because the engine reports an
  invalid blueprint as a non-zero exit carrying the verdict on stdout: executing would discard exactly
  the answer that matters. No verdict at all returns null — unknown, never an assumed pass.
- **Blueprint lifecycle events — `blueprint_created` / `blueprint_updated` / `blueprint_removed`.**
  `WriteRaw`, `Create` and `Remove` emit them with the caller's provenance threaded through. A failed
  emit never fails the file operation: the bytes are already committed and valid, so reporting an error
  would claim a save that did happen did not. Payloads carry name, tier, override state and runtime —
  never the file body or a diff.
- `RecordingEventManagementService` (test-only) — an `IEventManagementService` that records emissions so
  a test can assert the event type, provenance, and parameters an authority actually threaded through.

### Changed
- **The event data hierarchy grew a subject-neutral root, `KgsmEventDataBase`.** `EventDataBase` was
  instance-scoped by contract ("all events have an InstanceName"), which held only because every event so
  far happened to concern an instance. Blueprints are the first subject that is not one, and forcing them
  through `InstanceName` would fabricate an instance relationship that does not exist. `EventDataBase`
  (instance-scoped) and the new `BlueprintEventDataBase` (blueprint-scoped) are now siblings beneath the
  root, which carries the emission metadata every event has regardless of subject. Adding the next
  non-instance subject means adding one sibling rather than revisiting the root again.
  - **Interface change:** `IEventService.RegisterHandler<T>`'s constraint moves from `EventDataBase` to
    `KgsmEventDataBase`. Call sites are typed per-event and are unaffected; an IMPLEMENTOR of
    `IEventService` (including a test fake) must restate the new constraint to compile.
  - **Interface change:** implementors of `IBlueprintFiles` must add `ReadRaw`/`WriteRaw`, and `Remove`
    gains optional `actor`/`origin` parameters. `BlueprintFiles`'s constructor now also takes
    `IBlueprintService` and `IEventManagementService`.

### Fixed
- `EventServiceTests.Initialize_SubscribesToSocketAndStartsListening` asserted a call made on a
  fire-and-forget background task the moment `Initialize` returned, so it failed intermittently under a
  loaded scheduler. It now waits for the listener to actually start.
- **Lifecycle verbs get their own timeout tier — `KgsmTimeoutOptions.Lifecycle` (default 5 minutes).**
  `start`/`stop`/`restart` ran on the 30s `Default` tier, but a stop writes the instance's stop command
  and drains for up to its `stop_command_timeout_seconds` before the supervisor hard-kills. With the
  shipped default of 30s the two deadlines coincided exactly, so `ProcessRunner` killed the KGSM process
  tree at the very moment the stop was completing: the caller was told the stop FAILED, and — because
  killing KGSM tore down the control-socket connection — the watchdog's own stop was aborted mid-drain
  too. The new tier sits above KGSM's internal ceilings (60s for a start, 120s for a stop) so the inner
  timeout is always the one that fires.

### Added
- `IBlueprintFiles.Render(draft)` + `IBlueprintFiles.TryParse(yaml)` — a matched render↔parse pair for
  the native blueprint YAML template, so an authoring surface can show a user the editable draft text and
  read their edits back. `Render` is the exact string `Create` writes, but pure (no filesystem, no engine);
  `TryParse` is its inverse — a deterministic, AOT-safe line parser (no reflection-based YAML library),
  tolerant of light hand-edits (unquoted/double-quoted scalars, whole-line comments) and preserving
  `$instance_*` placeholders and colons inside quoted scalars. Structural checks only (safe name, native
  runtime, required `executable_file`); semantic validity stays the engine's authority via readback.
- `IWatchdogClient.ForgetAsync(instanceName)` — deregisters an instance from the watchdog entirely
  (supervision table entry, cgroup, boot-autostart intent, persisted restart counters), mapping to the
  daemon's new `DELETE /instance/{name}` (kgsm-watchdog 1.9.0). This is the typed path for the uninstall
  counterpart: without it there was no way to un-supervise an instance, so the daemon held a
  `desired=running` record for every uninstalled native server forever. Idempotent — an unknown name is
  a successful no-op (the instance's kgsm spec is normally already deleted by the time it is called);
  `Ok = false` (409) means the instance is still running and was deliberately NOT deregistered, rather
  than orphaning the process.
  - **Interface change:** implementors of `IWatchdogClient` (test fakes included) must add the member.

### Added
- `IBlueprintFiles` (+ impl `BlueprintFiles`) — the write-side authority for native-runtime
  blueprint files: `Create(NativeBlueprintDraft, overwrite)` templates a draft into a valid
  `<name>.bp.yaml` string and atomically writes it into the user blueprints directory; `Remove`
  deletes one from the user dir ONLY (structurally incapable of touching the read-only system
  blueprints dir); `Exists` checks without reading. The SECOND kgsm-lib service that does direct
  `System.IO` (the first is `IInstanceFiles`, whose shape this mirrors: atomic temp→fsync→rename
  write, a jail, `FileOpResult`/`FileOpOutcome` outcome-not-exception). No semantic validation —
  only structural checks (a required `native.executable_file`, a safe lowercase-slug `name`); the
  engine stays the schema authority (validated by reading back through `IBlueprintService.GetInfo`
  in a later phase). The jail root — the user blueprints directory — is learned from the engine
  fresh on every call from `kgsm --paths --json`'s `user.KGSM_USER_BLUEPRINTS_DIR` (deserialized to
  the new `KgsmPaths` model via `KgsmJsonContext`), never re-derived from XDG rules in C#. The YAML
  is a deterministic string template (no YamlDotNet/reflection-based serializer — Native-AOT-safe),
  field order matching `templates/blueprint.tp`; every string scalar is single-quoted (YAML's one
  escape, doubling an embedded `'`), and every nullable field renders the literal `null` — never a
  fabricated placeholder. New `NativeBlueprintDraft`/`NativeBlueprintMetadataDraft`/
  `NativeBlueprintNativeDraft` DTOs (`Core/Models/NativeBlueprintDraft.cs`) and two new
  `FileOpOutcome` members (`BlueprintsDirUnavailable`, `InvalidDraft`) reusing the existing
  `FileOpResult`/`FileOpOutcome` channel from `IInstanceFiles`. Wired onto
  `IKgsmClient.BlueprintFiles` and `AddKgsmServices`. Phase 1 of the assistant-blueprint-authoring
  plan (`assistant-blueprint-authoring-plan.md`) — kgsm engine untouched.
- `IInstanceFiles` (+ impl `InstanceFiles`) — the single jailed filesystem authority for an
  instance's working directory: `List`, `Read`, `Write` (create/overwrite), `Delete`, `Rename`.
  The first kgsm-lib service that does direct `System.IO` (every other service shells the
  `kgsm` engine) and the first P/Invoke in the repo (`Native/LibC.cs`, a Native-AOT-safe
  `LibraryImport` `lstat`/`stat` wrapper with a blittable exact-size struct). The project now
  sets `<AllowUnsafeBlocks>` — required unconditionally by the `LibraryImport` source
  generator regardless of parameter shape (confirmed empirically), orthogonal to AOT/trim
  safety; no hand-written pointer code. The jail is ported from kgsm-api's `InstanceFileService` (the
  stronger of the ecosystem's two prior jails, `instance-filesystem-authority-plan.md`): a
  full POSIX-realpath canonicaliser resolving symlinks at EVERY path component (not just the
  leaf), a 64-hop loop guard, NUL-path rejection, and exact-root-or-descendant ordinal
  containment. Only regular files are opened for read/write; directories are only listed (or,
  with `DeleteOptions.AllowDir`, deleted when empty — never recursive); FIFO/socket/device is
  never opened. Binary detection is an 8 KB NUL scan plus a full strict-UTF-8 decode; writes
  are atomic (temp file → fsync → mode-preserve → rename, never truncate-in-place); reads and
  writes carry an sha256 etag for optimistic concurrency (`WriteOptions.ExpectedEtag` →
  `EtagMismatch` on drift); `WriteOptions.Backup` writes a sibling `.kgsmbak` before
  overwriting. Size caps are always a caller-supplied parameter, never a lib-wide default.
  New `FileOpResult`/`FileOpResult<T>` result type and `DirListing`/`FileEntry`/`FileKind`/
  `FileContent`/`FileStat`/`WriteOptions`/`DeleteOptions`/`RenameOptions` DTOs
  (`Core/Models/InstanceFileModels.cs`) — deliberately NOT registered in `KgsmJsonContext`,
  in-process only (the firewall-result precedent). Wired onto `IKgsmClient.InstanceFiles` and
  `AddKgsmServices`. Phase 1 of the cross-repo instance-filesystem-authority plan.
- `IWatchdogClient` gains the watchdog's on-demand UPnP control surface: `GetUpnpAsync`,
  `OpenUpnpAsync`, and `CloseUpnpAsync` (the typed client for the daemon's `GET /upnp/{name}` and
  `POST /upnp/{name}/open|close`). `GetUpnpAsync` returns `null` only when the daemon is unreachable and
  otherwise a `WatchdogUpnpList` whose `State` distinguishes a real query (`"queried"`, mappings possibly
  empty) from an inability to reach the router (`"unavailable"`) — the latter is never presented as "no
  forwards". `OpenUpnpAsync` accepts an optional explicit `PortMapping` set (else the instance's own
  ports) and `CloseUpnpAsync` removes them; both return a `WatchdogUpnpActionResult` whose `Outcome` is
  the honest three-way `applied` / `skipped` / `failed`, and both stamp the emitted audit event with an
  `origin` (default `control`). New `WatchdogUpnpMapping` / `WatchdogUpnpList` / `WatchdogUpnpActionResult`
  / `WatchdogUpnpOpenRequest` models, registered reflection-free in `KgsmJsonContext` (Native-AOT-safe).
- `IEventService.RegisterRawHandler(Func<EventWrapper, Task>)`: a catch-all hook that fires
  with the full envelope for every event the socket delivers, including event types with no
  `RegisterHandler<T>` mapping — the foundation for a neutral, raw event-history persister
  (kgsm-monitor) that never silently drops a new engine event type. Fires before typed
  dispatch and never suppresses it; each raw handler invocation is isolated in its own
  try/catch so a throwing handler can't stop the socket read loop or block other handlers.
- `TheKrystalShip.KGSM.Events.AuditId.ForEvent(EventWrapper)`: a pure, deterministic
  content-derived event id (`"evt_" + 16-hex-char SHA-256 digest`, no `Guid`/randomness) —
  hashes hostname + timestamp (unix ms) + event type + the `Data` payload's `InstanceName` +
  a digest of `Data`'s raw JSON text. Lets two independent computations of the same event
  (e.g. kgsm-monitor persisting it and kgsm-api's live handler correlating it) agree on the
  same id with zero coordination.
- `Instance.CpuPriority` (string?) and `Instance.MemoryCapMb` (int?) — expose the new per-instance
  resource-cap config keys (`cpu_priority`/`memory_cap_mb`) for watchdog cgroup enforcement.
- `IWatchdogClient.SetCpuPriorityAsync` — live-applies a CPU priority to a running instance's cgroup
  (low/normal/high → cpu.weight 50/100/400); returns `Ok=false` (not an exception) when the cgroup
  is absent (not running).
- `Instance.ScheduledRestart`, `RestartTime`, `RestartDay`, `Timezone` — expose the new schedule
  config keys for kgsm-scheduler to read.
- `IWatchdogClient.RestartAsync` — atomic intentional restart via `POST /restart/{name}?origin=`.
- `Instance.AutoBackupOnRestart` (bool?), `BackupRetention` (int?) — expose the Phase 4
  auto-backup config keys for kgsm-scheduler to read.
- Added `CrashRestart` (bool?) and `CrashMaxRestarts` (int?) to `Instance` model for per-instance crash-restart policy (Phase 6)
- `IInstanceService.PruneBackups(instanceName, keepN, actor, origin)` — prune old backups
  via the new `instances prune-backups --keep=N` kgsm primitive. Called by the scheduler
  after each auto-backup to enforce the retention policy.

## [1.31.0] - 2026-07-03

### Added
- `IWatchdogClient.EnableAsync(name)` / `DisableAsync(name)`: add/remove an instance in the
  watchdog's persisted boot-autostart set (POST `/enable`·`/disable`; idempotent — an
  already-enabled/disabled name returns `Ok == false` (409), never throws).
- `IWatchdogClient.GetEnabledNamesAsync()`: query the current boot-autostart set (GET `/enabled`);
  empty list when none, never null. Phase 1 of the KGSM Settings milestone (per-instance autostart).

## [1.30.0] - 2026-07-01

### Added
- `WatchdogPlayer` DTO: mirrors the watchdog's `PlayerSession` record for the `GET /players`
  endpoint response.
- `IWatchdogClient.GetAllPlayersAsync()`: fetches the live player session map from the watchdog.
  Returns `Dictionary<string, WatchdogPlayer[]>?` keyed by instance name, or `null` when the
  daemon is unreachable.

## [1.28.0] - 2026-06-30

### Added
- Initial versioned release.
