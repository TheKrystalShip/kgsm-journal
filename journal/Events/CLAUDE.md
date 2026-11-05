# What an event *is*: `KgsmEventCatalog`

**`KgsmEventCatalog` is the one registry of what the engine emits.** For each event it holds the class
the payload deserializes into (`PayloadType`), its subject, whether it is a `Fact` or a `Phase` (a step
inside a multi-step operation that has its own fact event), whether it reports a `Success` or a
`Failure`, and per payload field what kind of data that field holds. It lives here because it is a
property of the engine's events, and because one registry keeps every consumer that renders the journal
identical — no two surfaces can disagree about whether a player's network address may be shown.

- **Dispatch reads it.** `EventService` deserializes into `Describe(type).PayloadType`, so **an event
  that can be dispatched is necessarily one that has been classified** — there is no second table to
  fall out of step with. Add an event by adding a descriptor; there is nowhere else to register it. The
  `Instance<TData>` / `BlueprintEvent<TData>` helpers are constrained to the matching payload base, so
  an event's subject and its payload's own base cannot disagree either.
- **It states facts and never policy.** `Phase` does not mean "hide this"; `Personal` does not mean
  "refuse this". A consumer decides what to do with a fact, and two consumers are allowed to decide
  differently — the Control Panel shows a player's address and the Discord bot does not, both
  deliberately. **The moment a permission lands in the catalog, every surface inherits whichever one
  wrote the rule**, and the surfaces lose the right to differ. This is the constraint to defend when
  extending it.
- **`FieldSensitivity` is what the data is, not who may see it.** `Public` is safe wherever events are
  read; `Personal` identifies a natural person (`PlayerAddr`); `Privileged` is content that acts on a
  server and may carry a credential (`Command`); `Conditional` may identify a person depending on something
  the event does not carry — a moderation `Target` is an address, a name or an id according to the
  blueprint, and **a consumer that cannot resolve which must treat it as `Personal`**.
  `InstanceModerationDataBase.Target` already refuses to classify itself here; the catalog refuses on
  the same grounds rather than producing a second answer that could disagree with the template.
- **`FieldShape` is separate from sensitivity, and both matter.** `SessionKey` is `Public` and `Opaque`
  — nothing renders it for want of meaning rather than for privacy. `Ports` is structured, so a generic
  renderer that flattens it puts JSON in a sentence.
- **`Describe` never returns null.** An unrecognised type comes back with `Known` false, its subject
  read off the engine's `<subject>_<verb>` naming, and **no fields**. An empty field list means *render
  nothing from the payload* — not that the payload is empty. An event nobody has classified may carry
  anything, and a consumer that prints unclassified fields is one engine release away from publishing
  something it should not.
- **The drift tests are the whole point.** Every declared payload property is classified, no descriptor
  names a field the payload lacks (`KgsmEventCatalogTests`), and every event data class in the assembly
  is named by some descriptor (`EventDeserializationTests.EveryEventDataType_HasAMappingEntry` — the one
  that catches a payload class added and never wired, which would be dropped at runtime as an unknown
  type). A new event type or a new field **fails this build** until somebody classifies it, which is the
  only moment anyone is thinking about it. They reflect over the data classes; the catalog itself is
  static data, so the library stays reflection-free for its AOT consumers.
- Field classification walks up through intermediate bases (the moderation events carry theirs on a
  shared one) and stops before `EventDataBase`/`BlueprintEventDataBase`/`KgsmEventDataBase` — the
  subject and the envelope are structural, not payload.
- The descriptor table is built in a **static constructor**, not a field initializer. The shared field
  definitions are static initializers themselves and run in textual order, so building the table from a
  field initializer reads every one of them before it is assigned.

`AuditId` (here) turns an event's position into its id; what that rests on is
`../Services/CLAUDE.md`.
