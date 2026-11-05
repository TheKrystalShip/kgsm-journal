# The journal package

The event journal: the writer, the reader, the conformance checker beside the writer that has to satisfy
it, and `LeafLifecycle`.

**A run of NUL bytes in a segment is a hole, not a line.** The filesystem records the file as longer
than the bytes it has written, and a machine that goes down in between leaves zeros standing where an
append was going to be. Every reader passes lines through `JournalLine.WithoutHole` (`Events/`) first:
the hole carries no newline, so it runs into the next append and a reader would otherwise throw away a
whole event that landed. The bytes the hole replaced are gone and nothing reconstructs them — offsets
are taken from the line as it sits on disk, so healing one never moves an id.

`EventJournalWriter.TimestampFormat` and `SchemaVersion` are the constants the writer produces and the
conformance check verifies. One definition each — a second copy is a second thing to bump.

## Conformance (`Conformance/`, `TheKrystalShip.KGSM.Conformance`)

It reads what producers actually wrote and reports where it does not match the contract. The rules are
catalogued in `ConformanceRule`. It is **mechanism only** — no rule looks at an event type or a payload
field, because what a producer records and when is its own business.

`JournalConformance.CheckHost` takes journals as `(producer, directory)` pairs and checks them
individually plus the one thing only comparison reveals: that every journal on a host names the same
host. **It names no leaf.** `HostJournalConformanceTests` hands it the same `JournalDiscovery` scan every
consumer uses, so a producer added later is covered the moment its journal exists.

- **An old line is allowed to look old.** A line records what the build that wrote it produced, so a
  journal's history holds shapes a current build would no longer write. A host check samples the
  *newest* line per journal; the sample size is the caller's, which is why it is a parameter.
- **An empty scan fails.** A clean report over nothing is indistinguishable from a clean report over a
  host. A machine that is not a KGSM host sets `KGSM_CONFORMANCE_SKIP_HOST`; anything else fails, and
  `HostReport.Describe()` always states what it read.
- **A rule needs a test on both sides.** `Every_rule_the_checker_can_report_is_one_this_suite_exercises`
  fails when a rule is added without one, and the "does not fire" half is what keeps the check usable: a
  bare `heisen` actor and an explicit `"Origin":null` are correct, and a rule that reported them would be
  switched off within a day.

## A leaf reporting on itself (`Lifecycle/`, `TheKrystalShip.KGSM.Lifecycle`)

How a leaf says `leaf.ready`, `leaf.degraded`, `leaf.recovered`, `leaf.stopping`.

**It reports transitions, not states.** A leaf calls these from a polling loop without tracking what it
has already said; the emitter decides what changed. So most of its value is in what it declines to
write, and that is the part to preserve when changing it.

- **`MarkReady` takes the leaf's own readiness signal, never the host's.** `ApplicationStarted` fires
  once every hosted service has started — before a supervisor has joined its slice, before a gateway
  has connected, before a sampler has a frame. Wiring it to the host lifecycle would report every leaf
  ready before it was, which is why there is no shared hosted-service adapter and no
  `Microsoft.Extensions.Hosting` dependency.
- **A component already degraded is a no-op even when the detail differs.** Deliberate: a backend
  returning a different error string on each retry would otherwise turn one outage into a stream.
- **Keep the component set bounded.** A component id built from a guild, a mount or an instance makes
  the dedup dictionary grow without limit. Name the class of thing; put the offenders in `Detail`.
- **The leaf writes no field names.** The payload is composed inside the emitter, so these events have
  one writer however many leaves emit them. Names live in `LeafLifecycleFields`; the payload classes
  bind to those constants by `JsonPropertyName`, and `LeafLifecycleContractTests` checks the binding
  against what an emitter actually wrote rather than against a list.
- **No payload names a leaf or carries a version.** The producer comes from the journal the line was
  read out of, and `ProducerVersion` is on every envelope. That is why these do not derive from
  `ServiceEventData` — its `Leaf` id is required, and correct, for kgsm-api's `service_*` events about
  *other* leaves, which are the opposite direction from these.
