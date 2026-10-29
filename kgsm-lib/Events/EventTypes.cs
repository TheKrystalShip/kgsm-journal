using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// The root of every event data type — the emission metadata every KGSM event carries, independent of
/// what the event is ABOUT. Its subject-specific subclasses name that subject:
/// <see cref="EventDataBase"/> for instance-scoped events, <see cref="BlueprintEventDataBase"/> for
/// blueprint-scoped ones. A new kind of subject (host-scoped, leaf-scoped) adds one more sibling here
/// rather than borrowing a subject it does not have.
/// </summary>
public abstract class KgsmEventDataBase
{
    /// <summary>
    /// Gets or sets when the event was emitted (UTC). Populated from the event
    /// envelope's top-level <c>Timestamp</c> by <c>EventService</c>, not from the
    /// per-event <c>Data</c> payload. <see langword="null"/> means the emitter did
    /// not supply one (a pre-enrichment KGSM) — never a fabricated time.
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets who triggered the event (the audit principal). Populated from
    /// the envelope's top-level <c>Actor</c> by <c>EventService</c>. KGSM takes it
    /// from <c>$KGSM_EVENT_ACTOR</c> (caller-supplied) as <c>provider:name</c>;
    /// <see langword="null"/> means the emitter did not supply one.
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Gets or sets the surface that drove the event (<c>ui</c>, <c>assistant</c>,
    /// <c>discord</c>, <c>system</c>, <c>api</c>) — the companion to <see cref="Actor"/>
    /// (who) answering "through which surface". Populated from the envelope's top-level
    /// <c>Origin</c> by <c>EventService</c>. KGSM takes it from <c>$KGSM_EVENT_ORIGIN</c>
    /// (caller-supplied); unlike the actor there is no honest fallback for a bare CLI
    /// call, so <see langword="null"/> means no surface was declared — never fabricated.
    /// </summary>
    public string? Origin { get; set; }
}

/// <summary>
/// Base class for instance-scoped event data — every event whose subject is one game server instance.
/// The <see cref="InstanceName"/> identifies it.
/// </summary>
public abstract class EventDataBase : KgsmEventDataBase
{
    /// <summary>
    /// Gets or sets the name of the instance associated with the event.
    /// </summary>
    public string InstanceName { get; set; } = string.Empty;
}

/// <summary>
/// Base class for blueprint-scoped event data — every event whose subject is a blueprint file rather
/// than an instance. Blueprints exist independently of any instance, so these carry a
/// <see cref="BlueprintName"/> where instance events carry an <c>InstanceName</c>.
/// </summary>
public abstract class BlueprintEventDataBase : KgsmEventDataBase
{
    /// <summary>
    /// Gets or sets the name of the blueprint the event is about (the file's basename without
    /// <c>.bp.yaml</c>).
    /// </summary>
    public string BlueprintName { get; set; } = string.Empty;
}

/// <summary>
/// Base class for library-scoped event data — every event whose subject is a placement root rather
/// than an instance or a blueprint. A library outlives the instances placed in it and exists before
/// any of them, so these carry a <see cref="LibraryName"/> of their own.
/// </summary>
public abstract class LibraryEventDataBase : KgsmEventDataBase
{
    /// <summary>
    /// Gets or sets the name of the library the event is about — its registry name, which is what
    /// <c>--library</c> takes.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;
}

/// <summary>
/// Represents the wrapper for events received from KGSM — the top-level envelope
/// around each event's <see cref="Data"/> payload. Mirrors the wire shape emitted
/// by KGSM's <c>_build_event_payload</c>: <c>EventType</c>, <c>Data</c>, and the
/// emission metadata (<c>Timestamp</c>, <c>Actor</c>, <c>Origin</c>, <c>Hostname</c>,
/// <c>KGSMVersion</c>).
/// </summary>
public class EventWrapper
{
    /// <summary>
    /// Gets or sets the line's own name — the id its producer minted when it wrote the line
    /// (reference/event-envelope.md). <see langword="null"/> when the line carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A lowercase hyphenated UUIDv7, minted at write time and never derived from the content: two
    /// identical events in the same second are two events, and anything digest-shaped folds them into
    /// one. Being a v7 it sorts the way the journal does, so a range over ids is a range over time.
    /// </para>
    /// <para>
    /// <b>Null is unknown, never a mismatch.</b> Every line written before the field existed has no
    /// id and stays readable for as long as retention holds it, and a producer whose shell cannot mint
    /// one writes null rather than a lesser id. A reader that treats absence as a disagreement
    /// condemns the entire back catalogue.
    /// </para>
    /// <para>
    /// A <see cref="string"/> rather than a <see cref="Guid"/>: a malformed id costs the comparison
    /// it would have served, where a parse would cost the whole envelope — and losing an event to a
    /// bad field on it is a worse trade than carrying a field that cannot be compared.
    /// <see cref="Conformance.JournalConformance.IsWellFormedEventId"/> is where the shape is judged.
    /// </para>
    /// </remarks>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the type of the event.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data associated with the event.
    /// </summary>
    public JsonElement Data { get; set; }

    /// <summary>
    /// Gets or sets how much the producer says this event matters, or <see langword="null"/> when it
    /// does not say.
    /// </summary>
    /// <remarks>
    /// The producer's own judgement and the only honest source of one: the scheduler knows its
    /// retention sweep is routine and the engine knows an uninstall is not, and neither fact exists
    /// anywhere else. Absent is unknown — a reader treats it as such rather than substituting a
    /// default, which would be the reader's opinion wearing the producer's name.
    /// </remarks>
    public string? Severity { get; set; }

    /// <summary>
    /// Gets or sets how the event went, or <see langword="null"/> when the producer does not say.
    /// </summary>
    /// <remarks>
    /// Orthogonal to <see cref="Severity"/>: a backup created and a config key set are both routine
    /// and differ here, where an uninstall that worked and one that failed differ in severity.
    /// </remarks>
    public string? Outcome { get; set; }

    /// <summary>
    /// Gets or sets what happened, in one line, or <see langword="null"/> when the producer does not
    /// say.
    /// </summary>
    /// <remarks>
    /// Written when the event was raised, so it names things as they were called at the time. A
    /// rename afterwards leaves this line saying what the server was called when this happened, which
    /// is what stops a feed rewriting its own history.
    /// </remarks>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets when the event was emitted (UTC). <see langword="null"/> if the
    /// emitter did not include it.
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets who triggered the event (the audit principal). <see langword="null"/>
    /// if the emitter did not include it.
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Gets or sets the surface that drove the event (<c>ui</c>, <c>assistant</c>,
    /// <c>discord</c>, <c>system</c>, <c>api</c>) — the companion to <see cref="Actor"/>.
    /// KGSM takes it from <c>$KGSM_EVENT_ORIGIN</c>; <see langword="null"/> when the
    /// emitter declared no surface (e.g. a bare CLI call) — never fabricated.
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>
    /// Gets or sets the host KGSM emitted the event from. <see langword="null"/> if absent.
    /// </summary>
    public string? Hostname { get; set; }

    /// <summary>
    /// Gets or sets the KGSM version that emitted the event. <see langword="null"/> if absent.
    /// </summary>
    /// <remarks>
    /// The v0 spelling of <see cref="ProducerVersion"/>, which every producer writes. Read
    /// <see cref="EmittingVersion"/> rather than either field directly.
    /// </remarks>
    [JsonPropertyName("KGSMVersion")]
    public string? KgsmVersion { get; set; }

    /// <summary>
    /// Gets or sets the envelope schema version. <see langword="null"/> means v0 — an envelope
    /// written before the field existed, which is still on disk for as long as retention holds it.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="ProducerVersion"/>. One says how to read the line, the
    /// other says which build wrote it; a fleet of independently-deployed producers needs both, and
    /// a single field serving as both cannot answer either question reliably.
    /// </remarks>
    [JsonPropertyName("V")]
    public int? SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the version of the component that emitted the event. <see langword="null"/> if
    /// absent.
    /// </summary>
    public string? ProducerVersion { get; set; }

    /// <summary>
    /// The emitting component's version, whichever spelling the envelope used —
    /// <see cref="ProducerVersion"/> when present, else the v0 <see cref="KgsmVersion"/>.
    /// </summary>
    [JsonIgnore]
    public string? EmittingVersion => ProducerVersion ?? KgsmVersion;

    /// <summary>
    /// Gets or sets the operation this event is <em>part of</em> — the correlation token handed to
    /// the emitter, or minted by it. <see langword="null"/> when nothing correlated it.
    /// </summary>
    /// <remarks>
    /// An emitter may stamp a token it was <b>given</b> or one it <b>minted</b>, never one it
    /// <b>inferred</b>. This field asserts that the emitter was executing that operation, which is
    /// a causal claim; an observed coincidence goes in <see cref="During"/> instead. Nothing
    /// populates this yet.
    /// </remarks>
    public string? OpId { get; set; }

    /// <summary>
    /// Gets or sets the process lifetime this event belongs to — the run id the supervisor mints at
    /// spawn. <see langword="null"/> outside a supervised run.
    /// </summary>
    /// <remarks>
    /// Orthogonal to <see cref="OpId"/> and not a substitute for it: a crash-restart has a new run
    /// and no operation (nobody asked for it), while one update spans two runs. Nothing populates
    /// this yet.
    /// </remarks>
    public string? RunId { get; set; }

    /// <summary>
    /// Gets or sets the operations that were in flight when this event was established —
    /// co-incidence, measured, never causality. <see langword="null"/> when none were, or when the
    /// emitter does not track them.
    /// </summary>
    /// <remarks>
    /// The honest home for a relation an emitter <em>observed</em> rather than participated in: a
    /// threshold breach during an update knows it happened between two recorded events, and does
    /// not know the update caused it. Nothing populates this yet.
    /// </remarks>
    public string[]? During { get; set; }
}

/// <summary>
/// Event data for when an instance is created.
/// </summary>
public class InstanceCreatedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the blueprint name used to create the instance.
    /// </summary>
    public string Blueprint { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when instance directories are created.
/// </summary>
public class InstanceDirectoriesCreatedData : EventDataBase
{
}

/// <summary>
/// Event data for when instance files are created.
/// </summary>
public class InstanceFilesCreatedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance download starts.
/// </summary>
public class InstanceDownloadStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance download finishes.
/// </summary>
public class InstanceDownloadFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance download fails.
/// </summary>
public class InstanceDownloadFailedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is downloaded.
/// </summary>
public class InstanceDownloadedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance deployment starts.
/// </summary>
public class InstanceDeployStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance deployment finishes.
/// </summary>
public class InstanceDeployFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance deployment fails.
/// </summary>
public class InstanceDeployFailedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is deployed.
/// </summary>
public class InstanceDeployedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance's restart starts — it is going down and coming back. The matching
/// <see cref="InstanceRestartFinishedData"/> ends the run whatever its outcome;
/// <see cref="InstanceRestartedData"/> is the separate fact that the instance came back. A restart
/// runs the stop and the start internally, so neither the stop bracket nor the start/stop events fire
/// inside it — this pair spans the run, and <see cref="InstanceRestartStoppedData"/> marks its middle.
/// </summary>
public class InstanceRestartStartedData : EventDataBase
{
}

/// <summary>
/// Event data for the middle of a restart: the old run is down and the new one has not been spawned
/// yet. A step inside one operation rather than a standalone shutdown, which is why it is not
/// <see cref="InstanceStoppedData"/> — that one is the fact that somebody stopped a server, and a
/// restart is not that. What it carries is the several seconds to a minute during which the process
/// genuinely does not exist: without it the only word about the new run is
/// <see cref="InstanceRestartedData"/> at the very end, and until then a consumer can only keep
/// reporting the state from before the restart.
/// </summary>
public class InstanceRestartStoppedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance's restart run ends. Emitted on every outcome — it states that the
/// run ENDED, not that the instance came back (see <see cref="InstanceRestartedData"/>).
/// </summary>
public class InstanceRestartFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance's shutdown starts — the supervisor has asked the game to stop and
/// is waiting for it to drain. The matching <see cref="InstanceStopFinishedData"/> ends the run
/// whatever its outcome; <see cref="InstanceStoppedData"/> is the separate fact that the instance is
/// actually down.
/// </summary>
public class InstanceStopStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance's shutdown run ends. Emitted on every outcome — it states that
/// the run ENDED, not that the instance stopped (see <see cref="InstanceStoppedData"/>).
/// </summary>
public class InstanceStopFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance update starts.
/// </summary>
public class InstanceUpdateStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance update finishes.
/// </summary>
public class InstanceUpdateFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for an update run that ended without the version moving, for a reason — the download or
/// the deploy failed, the pre-update backup could not be taken, or the engine refused to overwrite a
/// running instance. It is what tells that outcome apart from the other way an update leaves the
/// version alone, which is finding nothing to do: both emit
/// <see cref="InstanceUpdateFinishedData"/> and no <see cref="InstanceVersionUpdatedData"/>, so
/// without this a refused update and a successful no-op are the same two lines, and a consumer that
/// settles the run on its bracket reports the refusal as a completed update.
/// </summary>
public class InstanceUpdateFailedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is updated.
/// </summary>
public class InstanceUpdatedData : EventDataBase
{
}

/// <summary>
/// Event data for when a newer version of an instance's game becomes available upstream. Emitted once
/// per version by <c>kgsm instances check-update &lt;instance&gt; --emit</c>: the engine records what it
/// found beside the instance, so the same upstream version is announced once and a check run by hand
/// never consumes an announcement. It states that an update EXISTS — <see cref="InstanceUpdatedData"/>
/// states that one was applied.
/// </summary>
public class InstanceUpdateAvailableData : EventDataBase
{
    /// <summary>
    /// Gets or sets the version currently installed.
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version found upstream.
    /// </summary>
    public string LatestVersion { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance version is updated.
/// </summary>
public class InstanceVersionUpdatedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the old version of the instance.
    /// </summary>
    public string OldVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the new version of the instance.
    /// </summary>
    public string NewVersion { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance installation starts.
/// </summary>
public class InstanceInstallationStartedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the blueprint name used for installation.
    /// </summary>
    public string Blueprint { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance installation finishes.
/// </summary>
public class InstanceInstallationFinishedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the blueprint name used for installation.
    /// </summary>
    public string Blueprint { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance is installed.
/// </summary>
public class InstanceInstalledData : EventDataBase
{
    /// <summary>
    /// Gets or sets the blueprint name used for installation.
    /// </summary>
    public string Blueprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the library the install landed in. Always stated: placement is
    /// resolved before a single directory is created, so the installer always knows it, and on a
    /// host with several disks a record of an install that cannot say which one it went onto is
    /// the record that host needs most.
    /// </summary>
    public string Library { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance left this host in a transfer: another machine holds it, and its
/// files, backups and registry entry here are gone.
/// </summary>
public class InstanceExportedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance arrived from another host in a transfer, under a fresh id.
/// </summary>
public class InstanceImportedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the blueprint the imported instance runs.
    /// </summary>
    public string Blueprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the library the instance landed in.
    /// </summary>
    public string Library { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance's files were moved into a different library.
/// </summary>
/// <remarks>
/// The instance is the same instance — only its files went anywhere. Both libraries are named
/// because a reader that learns only the destination cannot tell which disk just got its space
/// back, and emptying a disk before it is unplugged is the whole reason the verb exists.
/// <para>
/// The move starts the instance once on the new path to confirm it runs there, so an
/// <c>instance_started</c> and an <c>instance_stopped</c> land between the operation's beginning
/// and this event, with no bracket saying they belong to it. A consumer that settles run-state on
/// those alone reports the server as having been running mid-move.
/// </para>
/// </remarks>
public class InstanceMovedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the name of the library the instance came from. The literal
    /// <c>unregistered</c> when it was under a root this host holds no entry for — a measurement,
    /// not an absence.
    /// </summary>
    public string FromLibrary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the library the instance is now in.
    /// </summary>
    public string ToLibrary { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance is started.
/// </summary>
public class InstanceStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is ready.
/// This event is triggered when the instance is fully operational and ready for use.
/// </summary>
public class InstanceReadyData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is stopped.
/// </summary>
public class InstanceStoppedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is restarted.
/// </summary>
public class InstanceRestartedData : EventDataBase
{
}

/// <summary>
/// Event data for when the resident supervisor (kgsm-watchdog) detected that an
/// instance's process died unexpectedly while it was desired-running and is
/// auto-restarting it. An autonomous engine action — emitted with
/// <c>Actor == "system"</c> and <c>Origin == "system"</c> (no human surface drove it).
/// Distinct from <see cref="InstanceRestartedData"/>, which is a deliberate operator
/// restart.
/// </summary>
public class InstanceCrashedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the leader process exit code the supervisor read, as a string
    /// (the wire value). The literal <c>"unknown"</c> when the code could not be read
    /// — never a fabricated code.
    /// </summary>
    public string ExitCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the consecutive restart-attempt count at the moment of the crash
    /// (always ≥ 1), as a string.
    /// </summary>
    public string Restarts { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when the resident supervisor (kgsm-watchdog) gave up auto-restarting
/// an instance after exhausting its restart retries; the instance is down and staying
/// down (supervision phase = failed). This is the escalation signal. An autonomous
/// engine action — emitted with <c>Actor == "system"</c> and <c>Origin == "system"</c>.
/// </summary>
public class InstanceFailedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the last leader process exit code the supervisor read, as a string
    /// (the wire value). The literal <c>"unknown"</c> when the code could not be read
    /// (e.g. the respawn itself failed to start) — never a fabricated code.
    /// </summary>
    public string ExitCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the consecutive-failure count at give-up (the retries exhausted),
    /// as a string.
    /// </summary>
    public string Restarts { get; set; } = string.Empty;
}

/// <summary>
/// Event data for the start of a backup run — the archiving has begun. Archiving a large world is
/// minutes of work, and a scheduler drives it unattended, so the run needs to be visible while it
/// happens rather than only once <see cref="InstanceBackupCreatedData"/> lands at the end. The
/// matching <see cref="InstanceBackupFinishedData"/> ends it on every outcome.
/// </summary>
public class InstanceBackupStartedData : EventDataBase
{
}

/// <summary>
/// Event data for the end of a backup run, whatever its outcome — it states that the run ENDED, not
/// that an archive exists (see <see cref="InstanceBackupCreatedData"/>).
/// </summary>
public class InstanceBackupFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for the start of a restore run. Longer than a backup and rather more consequential: it
/// takes a safety backup of the current state, verifies the archive and then overwrites the
/// instance's data with it. The matching <see cref="InstanceRestoreFinishedData"/> ends it on every
/// outcome; <see cref="InstanceBackupRestoredData"/> is the separate fact that the data was replaced.
/// </summary>
public class InstanceRestoreStartedData : EventDataBase
{
}

/// <summary>
/// Event data for the end of a restore run, whatever its outcome — it states that the run ENDED, not
/// that anything was restored (see <see cref="InstanceBackupRestoredData"/>).
/// </summary>
public class InstanceRestoreFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance backup is created.
/// </summary>
public class InstanceBackupCreatedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the source of the backup.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the version of the backup.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance backup is restored.
/// </summary>
public class InstanceBackupRestoredData : EventDataBase
{
    /// <summary>
    /// Gets or sets the source of the backup that was restored.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the version of the backup that was restored.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when one named instance backup is deleted.
/// </summary>
/// <remarks>
/// Carries no version: the deleted backup's manifest is gone with it, and the instance's current
/// version would be a fact about the instance rather than about the backup.
/// </remarks>
public class InstanceBackupDeletedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the id of the backup that was deleted.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when retention swept an instance's old backups.
/// </summary>
/// <remarks>
/// One event covers the whole sweep, so it reports counts rather than the ids it removed — the ids
/// are exactly the ones the instance no longer lists. Distinct from
/// <see cref="InstanceBackupDeletedData"/> because the two answer different questions: a delete is
/// an operator naming one snapshot, a prune is policy running. A sweep that removed nothing emits
/// no event at all, so <see cref="Deleted"/> is never zero on a received event.
/// </remarks>
public class InstanceBackupsPrunedData : EventDataBase
{
    /// <summary>
    /// Gets or sets how many backups were actually removed — never how many were attempted.
    /// </summary>
    public int Deleted { get; set; }

    /// <summary>
    /// Gets or sets the retention window the sweep ran with (the number of most-recent prunable
    /// backups kept).
    /// </summary>
    public int Kept { get; set; }

    /// <summary>
    /// Gets or sets how many backups the sweep skipped because they were pinned.
    /// </summary>
    /// <remarks>
    /// Reported alongside what was deleted so the pair states what the policy actually did: without
    /// it, a sweep that removed nothing because everything was protected reads exactly like one that
    /// found nothing to remove. Pinned backups do not consume a <see cref="Kept"/> slot, so the two
    /// numbers do not sum to the store's size.
    /// </remarks>
    public int Pinned { get; set; }
}

/// <summary>
/// Event data for when a backup is put out of retention's reach.
/// </summary>
/// <remarks>
/// Retention is a policy an operator revises, so both directions are recorded. Pinning changes
/// nothing about the archive itself — why it was taken is a fact and stays as it was.
/// </remarks>
public class InstanceBackupPinnedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the id of the backup that was pinned.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when a pinned backup is handed back to retention.
/// </summary>
/// <remarks>
/// The direction that can cost data later: the backup becomes eligible for the next sweep, so a
/// store that keeps growing is answered by knowing who released what.
/// </remarks>
public class InstanceBackupUnpinnedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the id of the backup that was unpinned.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when instance files are removed.
/// </summary>
public class InstanceFilesRemovedData : EventDataBase
{
}

/// <summary>
/// Event data for when instance directories are removed.
/// </summary>
public class InstanceDirectoriesRemovedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is removed.
/// </summary>
public class InstanceRemovedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance uninstall starts.
/// </summary>
public class InstanceUninstallStartedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance uninstall finishes.
/// </summary>
public class InstanceUninstallFinishedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance uninstall fails.
/// </summary>
public class InstanceUninstallFailedData : EventDataBase
{
}

/// <summary>
/// Event data for when an instance is uninstalled.
/// </summary>
public class InstanceUninstalledData : EventDataBase
{
    /// <summary>
    /// The install nonce of the install that was removed, or <see langword="null"/> when the instance's
    /// library was offline and nothing of it could be read. A grant naming this nonce ends with the
    /// install; one naming a later install under the same name is a different instance's.
    /// </summary>
    public string? InstallNonce { get; set; }
}

/// <summary>
/// Event data for when an instance's host-firewall ports were opened via the
/// kgsm-firewall authority (firewall enable, or an install with firewall
/// management on). Only a confirmed open emits this — a down authority hard-fails
/// the action and emits nothing, so the event is never a fabricated outcome.
/// </summary>
public class InstancePortsOpenedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the ports that were opened, as the canonical range-preserving
    /// <see cref="PortMapping"/> array (the same shape <c>instances info --json</c>
    /// emits) — never an opaque UFW string.
    /// </summary>
    public List<PortMapping> Ports { get; set; } = [];
}

/// <summary>
/// Event data for when an instance's host-firewall ports were closed via the
/// kgsm-firewall authority (firewall disable, or uninstall). Only a confirmed
/// removal emits this — a down authority warns and emits nothing.
/// </summary>
public class InstancePortsClosedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the ports that were closed, as the canonical range-preserving
    /// <see cref="PortMapping"/> array — never an opaque UFW string.
    /// </summary>
    public List<PortMapping> Ports { get; set; } = [];
}

/// <summary>
/// Event data for when an instance's UPnP port mappings were opened on the local IGD
/// (router) — a <b>distinct</b> operation from the host-firewall
/// <see cref="InstancePortsOpenedData"/> (router NAT forward vs. ufw rule; a host can
/// have one without the other). Emitted by the kgsm-watchdog — the resident supervisor
/// owns UPnP because it is process-lifetime state — stamped <c>Actor == "system"</c> /
/// <c>Origin == "system"</c> (an autonomous daemon action). Only a confirmed mapping
/// (<c>upnpc</c> exited 0) emits this, never a fabricated outcome.
/// </summary>
public class InstanceUpnpOpenedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the ports that were forwarded, as the canonical range-preserving
    /// <see cref="PortMapping"/> array (the same shape <c>instances info --json</c>
    /// emits) — never an opaque UFW string.
    /// </summary>
    public List<PortMapping> Ports { get; set; } = [];
}

/// <summary>
/// Event data for when an instance's UPnP port mappings were closed on the local IGD
/// (router) — the close twin of <see cref="InstanceUpnpOpenedData"/>, distinct from the
/// host-firewall <see cref="InstancePortsClosedData"/>. Emitted by the kgsm-watchdog on
/// a deliberate stop, <c>Actor == "system"</c> / <c>Origin == "system"</c>. Only a
/// confirmed removal (<c>upnpc</c> exited 0) emits — a "nothing to delete" close (no
/// mapping existed) changes nothing and emits nothing.
/// </summary>
public class InstanceUpnpClosedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the ports whose forward was removed, as the canonical
    /// range-preserving <see cref="PortMapping"/> array — never an opaque UFW string.
    /// </summary>
    public List<PortMapping> Ports { get; set; } = [];
}

/// <summary>
/// Event data for a router forward that went missing while its instance kept running and was put
/// back by the kgsm-watchdog's periodic sweep. Distinct from <see cref="InstanceUpnpOpenedData"/>
/// because the two answer different questions: an open accompanies a bring-up, whereas this says the
/// mapping disappeared with nothing on this host asking for it. It is the only evidence a consumer
/// gets that a router discards mappings it accepted — an IGD may report a lease as infinite and drop
/// it anyway — and how often. Stamped <c>Actor == "system"</c> / <c>Origin == "system"</c>, and only
/// a confirmed re-open (<c>upnpc</c> exited 0) emits it.
/// </summary>
public class InstanceUpnpReassertedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the ports the sweep restored, as the canonical range-preserving
    /// <see cref="PortMapping"/> array. This is the subset the router was found to be missing, not
    /// the instance's whole configured set — a forward that was still in place is not reported as
    /// re-asserted.
    /// </summary>
    public List<PortMapping> Ports { get; set; } = [];
}

/// <summary>
/// Event data for when an instance's <c>.config.ini</c> had a single key changed
/// (via <c>kgsm config-set</c> / the lib's config setter). Both fields are always
/// present non-null strings — <see cref="EventDataBase.InstanceName"/> identifies the
/// instance and <see cref="Key"/> names the changed key. The new <em>value</em> is
/// intentionally never carried (secret hygiene — instance config can hold passwords
/// or tokens), so a consumer auditing this only learns that <see cref="Key"/> changed,
/// never to what.
/// </summary>
public class InstanceConfigChangedData : EventDataBase
{
    /// <summary>The config key that was changed. The value is intentionally never carried
    /// (secret hygiene — instance config can hold passwords/tokens).</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an instance's human-readable label changed — through
/// <c>kgsm instances rename</c>, or through a <c>config-set</c> of <c>display_name</c>, which emit
/// an <c>instance_config_changed</c> naming the key alongside this.
/// </summary>
/// <remarks>
/// <para><see cref="EventDataBase.InstanceName"/> is the instance's <em>id</em>, and a rename does
/// not change it — it is what a consumer holding a stale label looks that label up by.</para>
/// <para>Both labels ride along in full, unlike <see cref="InstanceConfigChangedData"/>, which
/// carries a key and never a value. A display name is decoration a person chose to be read; it
/// cannot hold a credential the way an arbitrary config value can, and a consumer that had only the
/// key would have to go and ask the engine for the new label to do the one thing this event exists
/// for.</para>
/// <para>Neither field is ever empty: an instance with no label of its own reads as its id, and that
/// is the value reported here — the same answer every other reader of the config gets.</para>
/// </remarks>
public class InstanceDisplayNameChangedData : EventDataBase
{
    /// <summary>The label the instance was shown as before the change.</summary>
    public string OldDisplayName { get; set; } = string.Empty;

    /// <summary>The label the instance is shown as now.</summary>
    public string NewDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when an arbitrary console command was sent to a running instance
/// (via <c>kgsm instances input</c> / the lib's <c>IInstanceService.SendInput</c>).
/// <see cref="EventDataBase.InstanceName"/> identifies the instance and <see cref="Command"/>
/// is the verbatim command text delivered to the server's console input.
/// </summary>
/// <remarks>
/// Unlike <see cref="InstanceConfigChangedData"/> (which carries the key but never the value),
/// this event carries the FULL command text — a deliberate choice so the audit trail records
/// exactly what an operator ran (a console command can do anything the game allows: ban/kick/op/…).
/// A console command can therefore contain a secret (e.g. an RCON login); the trade was accepted
/// because sending one takes <c>kgsm:server.console.write</c> and the trail's value is who-ran-what. A consumer that
/// must redact should do so at its own boundary.
/// </remarks>
public class InstanceInputSentData : EventDataBase
{
    /// <summary>The verbatim console command that was sent to the instance.</summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>
/// Event data for a message broadcast to everyone connected to an instance.
/// </summary>
/// <remarks>
/// Both halves are carried. <see cref="Message"/> is what a person wrote and what a
/// surface shows; <see cref="Command"/> is the console command it resolved to, which
/// names the blueprint template that ran and is what makes a broadcast that reached
/// nobody diagnosable.
/// <para>
/// This records that the engine wrote to the console, never that anybody read it.
/// </para>
/// </remarks>
public class InstanceAnnouncementSentData : EventDataBase
{
    /// <summary>The announcement text as it was given.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The resolved console command the game received.</summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>
/// Event data for when a player joined a running instance. For our kgsm-containers
/// images these are forwarded by the kgsm-watchdog — it tails the in-container event
/// channel and re-emits via kgsm-lib — stamped <c>Actor == "system"</c> /
/// <c>Origin == "system"</c> (an autonomous observation, not a human action). Native
/// detection (log-scraping via <c>NativeLogMatcher</c>) is the same shape. At least one
/// of <see cref="PlayerId"/> / <see cref="PlayerName"/> / <see cref="PlayerAddr"/> is
/// non-null (the emitting side enforces it — a session with no human-meaningful field
/// is not a roster entry).
/// </summary>
public class InstancePlayerJoinedData : EventDataBase
{
    /// <summary>
    /// Gets or sets the opaque, game-scoped stable player id (SteamID64 / Minecraft
    /// UUID / …) when the source provides one, otherwise <see langword="null"/>.
    /// Never fabricated — a source that gives only a display name leaves this null.
    /// </summary>
    public string? PlayerId { get; set; }

    /// <summary>
    /// Gets or sets the player's display label when the source provides one, otherwise
    /// <see langword="null"/>. Never fabricated. See <see cref="PlayerId"/> for the
    /// at-least-one-non-null guarantee.
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Gets or sets the player's real network address (<c>ip:port</c>) when the source
    /// exposes one (direct-socket games), otherwise <see langword="null"/>. Steam-relay
    /// / P2P games never expose a real address — never fabricated.
    /// </summary>
    public string? PlayerAddr { get; set; }

    /// <summary>
    /// Gets or sets the opaque per-session correlation token (<c>key ?? addr ?? id ??
    /// name</c>) — always a non-empty string. Not an identity, not an address; the
    /// roster of record (kgsm-api) keys on this rather than on the display name (names
    /// can collide across sessions).
    /// </summary>
    public string? SessionKey { get; set; }
}

/// <summary>
/// Event data for when a player left a running instance. The leave counterpart of
/// <see cref="InstancePlayerJoinedData"/> — same source, provenance, and nullable
/// identity rules.
/// </summary>
public class InstancePlayerLeftData : EventDataBase
{
    /// <summary>
    /// Gets or sets the opaque, game-scoped stable player id (SteamID64 / Minecraft
    /// UUID / …) when the source provides one, otherwise <see langword="null"/>.
    /// Never fabricated.
    /// </summary>
    public string? PlayerId { get; set; }

    /// <summary>
    /// Gets or sets the player's display label when the source provides one, otherwise
    /// <see langword="null"/>. Never fabricated. See <see cref="PlayerId"/> for the
    /// at-least-one-non-null guarantee.
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Gets or sets the player's real network address (<c>ip:port</c>) when the source
    /// exposes one (direct-socket games), otherwise <see langword="null"/>. Never
    /// fabricated.
    /// </summary>
    public string? PlayerAddr { get; set; }

    /// <summary>
    /// Gets or sets the opaque per-session correlation token (<c>key ?? addr ?? id ??
    /// name</c>) — always a non-empty string. Matches the token captured on this
    /// session's join, letting a consumer correlate the pair without touching
    /// display-name identity (which can collide).
    /// </summary>
    public string? SessionKey { get; set; }

    /// <summary>
    /// Gets or sets the disconnect reason when the game's log carries one (e.g.
    /// <c>RemoteConnectionClose</c>, <c>App_Min</c>), otherwise <see langword="null"/>.
    /// Never fabricated. Kick/ban classification of this vocabulary is deferred to a
    /// future version.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Base for the player-moderation audit events — an operator removed a player from a
/// running instance, blocked them, or lifted that block.
/// </summary>
/// <remarks>
/// These are their own event types rather than an <see cref="InstanceInputSentData"/>
/// record because the subject is a <em>player</em>, not a command: a consumer asking
/// "who was banned on this server" filters on the event type instead of pattern-matching
/// command text — text a hand-typed <c>instances input</c> could also produce, with no
/// moderation intent behind it.
/// </remarks>
public abstract class InstanceModerationDataBase : EventDataBase
{
    /// <summary>
    /// Gets or sets the player identity the operator supplied — whichever kind the
    /// game's blueprint template declared (<c>{ip}</c>, <c>{name}</c> or <c>{id}</c>).
    /// Carried verbatim and never classified here: the blueprint is where that meaning
    /// is declared, and re-deriving it would be a second answer that could disagree.
    /// A consumer that needs the kind reads it from the instance's template with
    /// <see cref="Core.Models.ModerationCommand.TryGetTargetKind"/>.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved console command that was delivered, so the trail
    /// records the literal effect beside its subject.
    /// </summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>
/// Data for the <c>instance_player_kicked</c> event — a player was disconnected from a
/// running instance. Emitted only once the command has been delivered.
/// </summary>
public class InstancePlayerKickedData : InstanceModerationDataBase
{
}

/// <summary>
/// Data for the <c>instance_player_banned</c> event — a player was disconnected and
/// blocked from reconnecting. Emitted only once the command has been delivered.
/// </summary>
public class InstancePlayerBannedData : InstanceModerationDataBase
{
}

/// <summary>
/// Data for the <c>instance_player_unbanned</c> event — a block was lifted. Emitted only
/// once the command has been delivered.
/// </summary>
/// <remarks>
/// The subject of an unban is by definition not connected, so this event is the record a
/// consumer offering "lift a ban" selects from — a live player roster cannot supply it.
/// </remarks>
public class InstancePlayerUnbannedData : InstanceModerationDataBase
{
}

/// <summary>
/// Data for the <c>blueprint_created</c> event — a blueprint file was written under a name that had no
/// user file before. The blueprint's CONTENT is deliberately absent: no event payload ever carries a file
/// body or a diff.
/// </summary>
public class BlueprintCreatedData : BlueprintEventDataBase
{
    /// <summary>
    /// Gets or sets which directory the file was written to. Always
    /// <see cref="BlueprintTier.User"/> — the system directory is never written to.
    /// </summary>
    public BlueprintTier Tier { get; set; }

    /// <summary>
    /// Gets or sets whether the new file shadows a same-named shipped blueprint.
    /// <see langword="null"/> when the emitter could not determine it — never a defaulted false.
    /// </summary>
    public bool? OverridesSystem { get; set; }

    /// <summary>
    /// Gets or sets the runtime the written blueprint declares (<c>native</c>/<c>container</c>).
    /// <see langword="null"/> when the emitter could not read one out of the file.
    /// </summary>
    public string? Runtime { get; set; }
}

/// <summary>
/// Data for the <c>blueprint_updated</c> event — an existing user blueprint file was overwritten. Carries
/// no content or diff, for the same reason as <see cref="BlueprintCreatedData"/>.
/// </summary>
public class BlueprintUpdatedData : BlueprintEventDataBase
{
    /// <summary>
    /// Gets or sets which directory the file was written to. Always
    /// <see cref="BlueprintTier.User"/>.
    /// </summary>
    public BlueprintTier Tier { get; set; }

    /// <summary>
    /// Gets or sets whether the file shadows a same-named shipped blueprint.
    /// <see langword="null"/> when the emitter could not determine it.
    /// </summary>
    public bool? OverridesSystem { get; set; }

    /// <summary>
    /// Gets or sets the runtime the written blueprint declares (<c>native</c>/<c>container</c>).
    /// <see langword="null"/> when the emitter could not read one out of the file.
    /// </summary>
    public string? Runtime { get; set; }
}

/// <summary>
/// Data for the <c>blueprint_removed</c> event — a user blueprint file was deleted.
/// </summary>
public class BlueprintRemovedData : BlueprintEventDataBase
{
    /// <summary>
    /// Gets or sets which directory the file was deleted from. Always
    /// <see cref="BlueprintTier.User"/> — a shipped blueprint can never be removed.
    /// </summary>
    public BlueprintTier Tier { get; set; }

    /// <summary>
    /// Gets or sets whether a shipped original is now in effect again. <see langword="true"/> means the
    /// removal reverted an override; <see langword="false"/> means the blueprint is gone entirely.
    /// <see langword="null"/> when the emitter could not determine it.
    /// </summary>
    public bool? RevertedToSystem { get; set; }
}

/// <summary>
/// Data for the <c>library_added</c> event — a placement root was registered, so the host has somewhere
/// new to put instances.
/// </summary>
public class LibraryAddedData : LibraryEventDataBase
{
    /// <summary>
    /// Gets or sets the absolute path of the library root. Always present: a library has no identity
    /// without one, so the emitter never leaves it unstated.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Data for the <c>library_removed</c> event — a placement root was deregistered. No file inside it is
/// touched, so an instance that lived there still exists on disk; it simply resolves to no registered
/// library until the root is registered again.
/// </summary>
public class LibraryRemovedData : LibraryEventDataBase
{
    /// <summary>
    /// Gets or sets the absolute path of the library root. Always present: a library has no identity
    /// without one, so the emitter never leaves it unstated.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>library.renamed</c> and <c>library.failed</c> — a library mutation kgsm-api issued that
/// the engine records nothing about: a rename, or an attempt the engine refused.
/// </summary>
/// <remarks>
/// One payload for both types, told apart by which type fired. <see cref="Error"/> is the engine's own
/// stderr and <see cref="ExitCode"/> its own number; a refused removal names the instances that blocked
/// it there.
/// </remarks>
public class LibraryOutcomeEventData : LibraryEventDataBase
{
    /// <summary>Gets or sets which mutation was asked for (<c>add</c>, <c>remove</c>, <c>rename</c>).</summary>
    public string Verb { get; set; } = string.Empty;

    /// <summary>Gets or sets the root's absolute path, when the request named one.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the name a rename moved the library to. Null for the other verbs.</summary>
    public string? NewName { get; set; }

    /// <summary>Gets or sets the engine's failure detail, verbatim. Null on a successful rename.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the engine's exit code, when a process ran to produce one.</summary>
    public int? ExitCode { get; set; }
}

/// <summary>
/// The base for what the DNS anchor reports about one name in the cluster's zone.
/// </summary>
/// <remarks>
/// The subject is the name, not an instance: a game's name points at a server without being an event
/// that server produced, and a node's own name belongs to no server at all.
/// </remarks>
public abstract class DnsNameEventDataBase : KgsmEventDataBase
{
    /// <summary>Gets or sets the fully qualified name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets what the name stands for, as the record's mark spells it (<c>member</c>, <c>capability</c>, <c>game</c>, <c>alias</c>, <c>capability-alias</c>).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the member the name belongs to.</summary>
    public string Member { get; set; } = string.Empty;

    /// <summary>Gets or sets the capability or instance the name stands for, empty for a node's own name.</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>dns.name.published</c> — a record was created for a name.
/// </summary>
public class DnsNamePublishedData : DnsNameEventDataBase
{
    /// <summary>Gets or sets the record type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets what the record points at.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets what the record pointed at before. Null on a first publication.</summary>
    public string? From { get; set; }
}

/// <summary>
/// Data for <c>dns.name.moved</c> — a name's record was corrected to what the registry says.
/// </summary>
public class DnsNameMovedData : DnsNameEventDataBase
{
    /// <summary>Gets or sets the record type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets what the record points at now.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets what the record pointed at before. Null when the anchor did not know.</summary>
    public string? From { get; set; }
}

/// <summary>
/// Data for <c>dns.name.withdrawn</c> — a name's record was deleted: the name was released, or can no
/// longer be pointed anywhere.
/// </summary>
public class DnsNameWithdrawnData : DnsNameEventDataBase
{
    /// <summary>Gets or sets why, in one line. Null for a name whose row was released.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// The shared shape of a threshold episode event — a measured value crossing a line this host watches,
/// and later coming back.
/// </summary>
/// <remarks>
/// <para>
/// The subject is a <b>host</b>, not an instance: the condition may be scoped to one server, but it is
/// the host's monitoring that established it, and a payload keyed on <c>InstanceName</c> would claim
/// otherwise for the many episodes that name no server at all.
/// </para>
/// <para>
/// <b>Raw values only, no domain vocabulary.</b> There is no summary sentence, no severity and no
/// formatted number here — a consumer renders those from the values, and freezing one consumer's
/// wording into the record would make every other consumer live with it. The Control Panel's phrasing
/// and a chat surface's are allowed to differ, and do.
/// </para>
/// </remarks>
public abstract class HostThresholdEventDataBase : KgsmEventDataBase
{
    /// <summary>Gets or sets the episode's stable id, so an open and its close can be paired.</summary>
    public string EpisodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the rule that was evaluated.</summary>
    public string RuleKey { get; set; } = string.Empty;

    /// <summary>Gets or sets what was measured (<c>cpu</c>, <c>memory</c>, <c>disk</c>, …).</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary>Gets or sets the scope the rule ran at — <c>server</c>, or a host-level scope.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the measurement was taken on within that scope — a mount point, a device —
    /// or <see langword="null"/> when the scope needs no further naming.
    /// </summary>
    public string? Ref { get; set; }

    /// <summary>
    /// Gets or sets the server this condition is about, or <see langword="null"/> for a host-wide one.
    /// </summary>
    public string? ServerId { get; set; }

    /// <summary>Gets or sets the line the value crossed.</summary>
    public double Threshold { get; set; }

    /// <summary>Gets or sets the worst reading across the whole episode.</summary>
    /// <remarks>
    /// The honest justification for the episode having existed, as opposed to whatever the value
    /// happened to be at either end of it.
    /// </remarks>
    public double PeakValue { get; set; }

    /// <summary>Gets or sets the worst band the episode reached (<c>warn</c>, <c>danger</c>).</summary>
    public string PeakBand { get; set; } = string.Empty;

    /// <summary>Gets or sets when the episode opened, in unix milliseconds.</summary>
    /// <remarks>
    /// Carried explicitly rather than left to the envelope's timestamp: the envelope says when the line
    /// was written, and a reader placing the breach in the trail needs when the condition changed.
    /// </remarks>
    public long OpenedTs { get; set; }
}

/// <summary>
/// Data for the <c>host_threshold_breached</c> event — a measured value crossed a line.
/// </summary>
public class HostThresholdBreachedData : HostThresholdEventDataBase
{
    /// <summary>Gets or sets the reading that opened the episode.</summary>
    public double OpenValue { get; set; }

    /// <summary>Gets or sets the band the value was in when it opened.</summary>
    public string Band { get; set; } = string.Empty;
}

/// <summary>
/// Data for the <c>host_threshold_cleared</c> event — a firing condition stopped firing.
/// </summary>
/// <remarks>
/// A separate event rather than a mutation of the breach, because the journal is append-only and the two
/// are separate immutable facts. <b>Cleared does not always mean recovered</b> — see
/// <see cref="CloseReason"/>.
/// </remarks>
public class HostThresholdClearedData : HostThresholdEventDataBase
{
    /// <summary>Gets or sets when the episode closed, in unix milliseconds.</summary>
    public long ClosedTs { get; set; }

    /// <summary>
    /// Gets or sets the reading that closed it, or <see langword="null"/> when it ended without one
    /// being taken.
    /// </summary>
    public double? CloseValue { get; set; }

    /// <summary>
    /// Gets or sets why the episode ended.
    /// </summary>
    /// <remarks>
    /// Load-bearing, and never to be flattened into "recovered". A value that came back under its line
    /// and a rule that stopped being evaluated are different events, and an episode that ended because
    /// its rule was retuned, disabled or removed did <b>not</b> recover — the value was never observed to
    /// come down. A consumer that reports every close as a return to normal is reporting a measurement
    /// nobody took.
    /// </remarks>
    public string? CloseReason { get; set; }
}

// ---- the Control Panel's own facts ------------------------------------------------------------
//
// Signing in, an account's authority changing, a leaf being reconfigured. Nothing about a game
// server, and no engine command behind any of them — the API performs these itself, so it is the
// author and records them in its own journal.
//
// They are classified here rather than privately because the catalog is what every consumer reads a
// payload through: a field left unclassified renders nowhere, and these payloads carry the values
// most worth being careful with on this host.

/// <summary>
/// Base for an event about a KGSM account — signing in, authority changing, an identity attached.
/// </summary>
/// <remarks>
/// The subject is the account, never the person: an account survives a display name changing and is
/// what authority is actually resolved against, so a trail keyed on anything else stops answering
/// "what did this account do" the moment somebody renames themselves.
/// </remarks>
public abstract class AccountEventDataBase : KgsmEventDataBase
{
    /// <summary>
    /// Gets or sets the account's stable id, or <see langword="null"/> when the producer did not have
    /// the account row in hand.
    /// </summary>
    /// <remarks>
    /// Nullable because it honestly is: somebody acting on another person's account holds their id,
    /// while a sign-out holds only the identity in the caller's own token. Writing a blank or
    /// re-deriving one from the handle would put a value in the record that nothing looked up.
    /// </remarks>
    public string? UserId { get; set; }

    /// <summary>Gets or sets the account's username at the time of the event.</summary>
    /// <remarks>
    /// A convenience for reading the trail back, not the key. <see cref="UserId"/> is the identity;
    /// this is what it was called when this happened, which is the honest thing to show beside a row
    /// that may predate a rename.
    /// </remarks>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>auth.signed_in</c> and <c>auth.signed_out</c> — somebody's session began or ended.
/// </summary>
public class AuthSessionEventData : AccountEventDataBase
{
    /// <summary>Gets or sets the identity that arrived, as <c>provider:name</c>.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>Gets or sets the identity provider that proved who they are (<c>discord</c>, <c>local</c>, …).</summary>
    public string? Provider { get; set; }

    /// <summary>Gets or sets the session id, so a login and its logout pair up.</summary>
    public string? Sid { get; set; }

    /// <summary>Gets or sets the calling device's user agent, or <see langword="null"/> when it sent none.</summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Data for <c>auth_session_revoked</c> — one or more sessions were torn down before they expired.
/// </summary>
/// <remarks>
/// One event with a <see cref="Scope"/> rather than three types, because it is one fact told three
/// ways: sessions stopped being valid. Who was affected and who did it are already on the row.
/// </remarks>
public class AuthSessionRevokedData : AccountEventDataBase
{
    /// <summary>
    /// Gets or sets what was revoked — <c>self</c> (one of the caller's own), <c>all</c> (every
    /// session the caller holds), <c>other</c> (somebody else's), or <c>withdrawn</c> (ended because
    /// the account was switched off).
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the single session id revoked, or null when the revocation was a sweep.</summary>
    public string? Sid { get; set; }

    /// <summary>Gets or sets how many sessions the revocation ended, or null when it was not counted.</summary>
    public int? Count { get; set; }
}

/// <summary>
/// Data for <c>auth_locked_out</c> — a run of wrong passwords locked an account.
/// </summary>
/// <remarks>
/// <para>
/// Written when the lockout <em>begins</em>, never on the attempts it then refuses. An account under
/// attack is tried again immediately and would otherwise produce a row per guess — turning the one
/// record worth reading into the flood that buries it.
/// </para>
/// <para>
/// The account is real: a wrong username never reaches a lockout, because there is nothing to lock.
/// So this names somebody whose password was being guessed, which is what separates it from a failed
/// attempt and is the reason it is the failure worth a row.
/// </para>
/// </remarks>
public class AuthLockedOutData : AccountEventDataBase
{
    /// <summary>Gets or sets the identity the attempts were made against, as <c>provider:name</c>.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>Gets or sets how many consecutive failures the lockout followed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Gets or sets when the account can be tried again.</summary>
    public DateTimeOffset Until { get; set; }
}

/// <summary>
/// Data for <c>auth.admin.refused</c> — the auth anchor turned somebody away from an
/// administrator-only surface.
/// </summary>
/// <remarks>
/// <para>
/// One line stands for every refusal of the same surface, account and address within a minute:
/// <see cref="Count"/> is how many it stands for, so a scanner is one row rather than a flood.
/// </para>
/// <para>
/// Not an <see cref="AccountEventDataBase"/>: the refused party is frequently nobody the anchor could
/// identify, and the account is named by <see cref="Account"/> only when it was.
/// </para>
/// </remarks>
public class AuthAdminRefusedData : KgsmEventDataBase
{
    /// <summary>Gets or sets where the refusal happened: <c>document</c>, <c>token</c> or <c>route</c>.</summary>
    public string Surface { get; set; } = string.Empty;

    /// <summary>Gets or sets the path that was asked for, or the token endpoint for a token.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>Gets or sets the username it was refused to, or null when nobody was identified.</summary>
    public string? Account { get; set; }

    /// <summary>Gets or sets where the request came from, or null when that is not known.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets how many refusals this line stands for.</summary>
    public int Count { get; set; }
}

/// <summary>
/// Data for the <c>user.*</c> events — an account was provisioned, approved, disabled, deleted, or
/// had its password set.
/// </summary>
/// <remarks>
/// <b>Never carries a password</b>, in any form, hashed or otherwise. <c>user_password_changed</c>
/// records that a credential was set and by whom, which is the only signal an account takeover leaves;
/// the credential itself is not part of that fact.
/// </remarks>
public class UserAccountEventData : AccountEventDataBase
{
    /// <summary>Gets or sets the status the account held before, or null when it did not change.</summary>
    public string? FromStatus { get; set; }

    /// <summary>Gets or sets the status it holds after, or null when the event did not change it.</summary>
    public string? ToStatus { get; set; }

    /// <summary>
    /// Gets or sets whether the account's own holder did this, as opposed to somebody else acting
    /// on them. Null when the distinction does not apply to the event.
    /// </summary>
    /// <remarks>
    /// The whole point of recording a password change: somebody else setting yours reads completely
    /// differently from you setting it, and a row that could not tell them apart would report the
    /// takeover and the routine rotation identically.
    /// </remarks>
    public bool? ByHolder { get; set; }
}

/// <summary>
/// Data for <c>identity_linked</c> / <c>identity_unlinked</c> — an external identity was attached to
/// or detached from an account.
/// </summary>
/// <remarks>
/// A link is a privilege event: afterwards, whoever controls that provider account can sign in as
/// this one.
/// </remarks>
public class IdentityLinkEventData : AccountEventDataBase
{
    /// <summary>Gets or sets the provider the identity comes from.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets the identity's handle at that provider, as <c>provider:name</c>.</summary>
    public string Handle { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>auth.assignment.granted</c> / <c>auth.assignment.revoked</c> — an account was given a
/// role within a scope, or lost one.
/// </summary>
/// <remarks>
/// A revocation is recorded one assignment at a time even when a role's or an account's deletion took
/// it, so an access review reads what happened to each person rather than inferring it.
/// </remarks>
public class AssignmentEventData : AccountEventDataBase
{
    /// <summary>Gets or sets the assignment's id.</summary>
    public string AssignmentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the role's id.</summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>Gets or sets the role's name at the time, or null when it was not known.</summary>
    public string? Role { get; set; }

    /// <summary>Gets or sets where the role is held: <c>cluster</c>, <c>node:&lt;id&gt;</c> or an instance.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the authority version the change produced.</summary>
    public long AuthorityVersion { get; set; }
}

/// <summary>
/// Data for <c>auth.role.changed</c>, <c>auth.role.removed</c>, <c>auth.permission.changed</c> and
/// <c>auth.permission.removed</c> — a role or a permission was created, changed or deleted.
/// </summary>
public class AuthorityRecordEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the role's or permission's id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets what it is called, or null when it was not known.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the authority version the change produced.</summary>
    public long AuthorityVersion { get; set; }
}

/// <summary>
/// Data for <c>auth.catalog.changed</c> — actions arrived in or left the catalog of what can be granted.
/// </summary>
/// <remarks>
/// An action that arrives is in no permission until a person files it; one that leaves is gone from
/// every permission that held it.
/// </remarks>
public class CatalogEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the member whose report, or removal, changed the catalog.</summary>
    public string Member { get; set; } = string.Empty;

    /// <summary>Gets or sets the action ids that arrived.</summary>
    public string[]? Added { get; set; }

    /// <summary>Gets or sets the action ids no member declares any more.</summary>
    public string[]? Removed { get; set; }

    /// <summary>Gets or sets the authority version the change produced.</summary>
    public long AuthorityVersion { get; set; }
}

/// <summary>
/// Data for <c>auth.service.requirement.approved</c> / <c>auth.service.requirement.revoked</c> — an
/// action a service account needs was approved at a scope, or revoked.
/// </summary>
public class ServiceRequirementEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the service account's id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the service as <c>svc:&lt;component&gt;@&lt;member&gt;</c>, or null when it was not known.</summary>
    public string? Service { get; set; }

    /// <summary>Gets or sets the action required.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets where it is approved, or null when it was revoked.</summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets whether nobody decided it: approved because the account had never held it. A
    /// grant nobody chose is still one somebody should see.
    /// </summary>
    public bool Automatic { get; set; }

    /// <summary>Gets or sets the authority version the change produced.</summary>
    public long AuthorityVersion { get; set; }
}

/// <summary>
/// Data for <c>auth.application.changed</c>, <c>auth.application.removed</c>,
/// <c>auth.application.client.removed</c> and <c>auth.application.secret.rotated</c> — an application
/// that obtains tokens from the auth anchor, or one of its clients, was registered, changed or removed.
/// </summary>
/// <remarks>
/// About the access model rather than an account: an application and its clients decide who can be
/// handed a token at all, for every account at once.
/// </remarks>
public class ApplicationEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the application's id, which is also its action namespace.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets what it is called, or null when it was not known.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the client the change was to, or null when it was to the application's own fields.</summary>
    public string? Client { get; set; }
}

/// <summary>
/// Data for <c>auth.token.exchanged</c> / <c>auth.token.exchange_refused</c> — a client exchanged a
/// Discord credential for a token naming an account, or asked to and was refused.
/// </summary>
/// <remarks>
/// <see cref="AccountEventDataBase.UserId"/> and <see cref="AccountEventDataBase.Username"/> are null when
/// no account holds the identity: a refusal for an unknown account names nobody, and an id derived from
/// the identity would put a value on the row that nothing looked up.
/// </remarks>
public class TokenExchangeEventData : AccountEventDataBase
{
    /// <summary>Gets or sets the client that authenticated and asked.</summary>
    public string Client { get; set; } = string.Empty;

    /// <summary>Gets or sets the application the token is, or would have been, for.</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Gets or sets the Discord identity exchanged, as <c>provider:subject</c>, when it is known.</summary>
    public string? Identity { get; set; }

    /// <summary>
    /// Gets or sets the client acting for the person, when the token names it as actor; null when the
    /// person presented their own credential.
    /// </summary>
    public string? ActedBy { get; set; }

    /// <summary>
    /// Gets or sets why no token was given — <c>subject_invalid</c>, <c>discord_application</c>,
    /// <c>client_not_allowed</c>, <c>account_unknown</c>, <c>account_pending</c>, … — or null when one was.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Base for an event about a leaf service on this host.
/// </summary>
/// <remarks>
/// Each <c>service_*</c> event has its own payload rather than one shared class with mostly-null
/// properties: a field a given event can never carry is one every consumer has to be told how to
/// treat anyway, and the classification would end up describing a shape nothing writes.
/// </remarks>
public abstract class ServiceEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the leaf's id (<c>monitor</c>, <c>watchdog</c>, …).</summary>
    public string Leaf { get; set; } = string.Empty;

    /// <summary>Gets or sets the leaf's display name.</summary>
    public string? DisplayName { get; set; }
}

/// <summary>
/// Data for <c>service_connected</c> / <c>service_disconnected</c> — a leaf's runtime provisioning
/// was flipped, which changes the set of capabilities this host reports rather than anything running.
/// </summary>
public class ServiceProvisioningEventData : ServiceEventData;

/// <summary>
/// Data for <c>service_config_changed</c> — a configuration override was applied to a leaf.
/// </summary>
public class ServiceConfigChangedEventData : ServiceEventData
{
    /// <summary>
    /// Gets or sets the configuration keys the change touched. <b>Keys only, never values</b> — a
    /// leaf's configuration holds tokens and passwords.
    /// </summary>
    public string[]? Keys { get; set; }

    /// <summary>Gets or sets how the change ended (<c>applied</c>, <c>rejected</c>, …).</summary>
    public string? Outcome { get; set; }
}

/// <summary>
/// Data for <c>service_restarted</c> — a leaf's unit was restarted on its own, rather than as the
/// tail of a configuration apply.
/// </summary>
public class ServiceRestartedEventData : ServiceEventData
{
    /// <summary>Gets or sets the systemd unit that was restarted.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Gets or sets whether systemd performed it.</summary>
    /// <remarks>
    /// A refused restart is recorded as much as a performed one — it is exactly the case nobody was
    /// watching a screen for.
    /// </remarks>
    public bool Ok { get; set; }
}

/// <summary>
/// Base for an event a leaf writes about <b>its own</b> state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <see cref="ServiceEventData"/>, and carrying no leaf id.</b> Those events are
/// kgsm-api recording what was done <em>to</em> a leaf, so they must name which one; these are a leaf
/// reporting on itself, and which leaf that is comes from the journal the line was read out of. A
/// <c>Leaf</c> property here would be a second answer able to disagree with the first — the reader
/// already establishes the producer and can check it, where a field inside the payload is a claim it
/// cannot.
/// </para>
/// <para>
/// There is no version property either, for the same reason: the envelope's <c>ProducerVersion</c>
/// already carries the build on every line.
/// </para>
/// </remarks>
public abstract class LeafLifecycleEventData : KgsmEventDataBase;

/// <summary>
/// Data for <c>leaf_ready</c> — a leaf reports it can do its job.
/// </summary>
public class LeafReadyEventData : LeafLifecycleEventData
{
    /// <summary>
    /// Gets or sets how long the leaf took to become able, in milliseconds from process start.
    /// </summary>
    /// <remarks><see langword="null"/> when the process start could not be read — never estimated.</remarks>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.StartupMs)]
    public long? StartupMs { get; set; }

    /// <summary>Gets or sets what the leaf came up as, when it has something to say about it.</summary>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.Detail)]
    public string? Detail { get; set; }
}

/// <summary>
/// Data for <c>leaf_degraded</c> — a leaf is up, and one part of its job is not working.
/// </summary>
public class LeafDegradedEventData : LeafLifecycleEventData
{
    /// <summary>
    /// Gets or sets which part of the leaf's job stopped working.
    /// </summary>
    /// <remarks>
    /// The field that makes the event actionable, and the reason degradation is not a boolean: a leaf
    /// can be broken in two ways at once and recover from one of them.
    /// </remarks>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.Component)]
    public string Component { get; set; } = string.Empty;

    /// <summary>Gets or sets what is wrong, in a sentence somebody can act on.</summary>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.Detail)]
    public string? Detail { get; set; }
}

/// <summary>
/// Data for <c>leaf_recovered</c> — a part that was not working is working again.
/// </summary>
public class LeafRecoveredEventData : LeafLifecycleEventData
{
    /// <summary>Gets or sets the component named when it was reported degraded.</summary>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.Component)]
    public string Component { get; set; } = string.Empty;

    /// <summary>Gets or sets how long that component was broken, in seconds.</summary>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.DegradedForSec)]
    public long? DegradedForSec { get; set; }
}

/// <summary>
/// Data for <c>leaf_stopping</c> — a leaf is going away deliberately.
/// </summary>
/// <remarks>
/// There is no <c>leaf_stopped</c> counterpart. The last thing a process can write is that it is
/// stopping; a <c>leaf_ready</c> with no <c>leaf_stopping</c> before it is how an unclean exit is
/// established, and the journal is already the record that says so.
/// </remarks>
public class LeafStoppingEventData : LeafLifecycleEventData
{
    /// <summary>
    /// Gets or sets why the leaf is stopping (<c>signal</c>, <c>idle</c>, <c>reload</c>).
    /// </summary>
    /// <remarks>
    /// Load-bearing. <c>idle</c> is a socket-activated leaf's resting state rather than a fault, and
    /// <c>reload</c> is a leaf replacing itself in place without restarting what it supervises. A
    /// consumer that reports a leaf going away has to read this before it does.
    /// </remarks>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.Reason)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets how long the leaf ran, in seconds.</summary>
    /// <remarks><see langword="null"/> when the process start could not be read — never estimated.</remarks>
    [JsonPropertyName(Lifecycle.LeafLifecycleFields.UptimeSec)]
    public long? UptimeSec { get; set; }
}

/// <summary>
/// The base for what the assistant reports about its own conduct.
/// </summary>
/// <remarks>
/// <see cref="KgsmEventDataBase"/> rather than <see cref="ServiceEventData"/>, for the same reason
/// <see cref="LeafLifecycleEventData"/> is: <b>no payload names the producer</b>. The journal
/// directory a line was read from already answers that, and a field inside the payload would be a
/// claim a reader cannot check — able, therefore, to disagree.
/// </remarks>
public abstract class AssistantEventData : KgsmEventDataBase;

/// <summary>
/// Data for <c>assistant_claim_corrected</c> — a reply described work the turn never did.
/// </summary>
/// <remarks>
/// <b>Never carries the prompt or the reply.</b> The journal is readable by anything on the host
/// that can open the directory; a transcript belongs to the person who spoke it. What is here is
/// enough to count these and to find the conversation, and nothing more.
/// </remarks>
public class AssistantClaimCorrectedEventData : AssistantEventData
{
    /// <summary>Gets or sets which check found it (<c>unbacked_action</c>, <c>unsearched_web</c>).</summary>
    [JsonPropertyName(AssistantEventFields.Check)]
    public string Check { get; set; } = string.Empty;

    /// <summary>Gets or sets what was done about it (<c>re_prompted</c>, <c>corrected</c>).</summary>
    [JsonPropertyName(AssistantEventFields.Resolution)]
    public string Resolution { get; set; } = string.Empty;

    /// <summary>Gets or sets which net caught it (<c>review</c>, <c>outer</c>).</summary>
    [JsonPropertyName(AssistantEventFields.Net)]
    public string Net { get; set; } = string.Empty;

    /// <summary>Gets or sets the conversation it happened in.</summary>
    /// <remarks>
    /// Carried for correlation only. The key embeds the account it belongs to, which is why the
    /// catalog classifies it as identifying a person rather than as a bare token.
    /// </remarks>
    [JsonPropertyName(AssistantEventFields.ConversationId)]
    public string? ConversationId { get; set; }
}

/// <summary>
/// Data for <c>assistant_action_declined</c> — somebody reached for an action they do not hold.
/// </summary>
public class AssistantActionDeclinedEventData : AssistantEventData
{
    /// <summary>Gets or sets the tool that was refused.</summary>
    [JsonPropertyName(AssistantEventFields.Tool)]
    public string Tool { get; set; } = string.Empty;

    /// <summary>Gets or sets why (<c>authority</c>, <c>actions_disabled</c>).</summary>
    [JsonPropertyName(AssistantEventFields.DeclineReason)]
    public string DeclineReason { get; set; } = string.Empty;

    /// <summary>Gets or sets the instance the action would have touched, when it named one.</summary>
    [JsonPropertyName(AssistantEventFields.Instance)]
    public string? Instance { get; set; }
}

/// <summary>
/// Data for <c>assistant_action_proposed</c> — a mutation is staged and waiting on a person.
/// </summary>
/// <remarks>
/// Carries no handle. The handle <b>is</b> the capability that redeems the action, and a journal is
/// not where a capability goes.
/// </remarks>
public class AssistantActionProposedEventData : AssistantEventData
{
    /// <summary>Gets or sets what kind of action was staged.</summary>
    /// <remarks>
    /// The name, never the ordinal. Retired members leave gaps in that enum, so an ordinal written
    /// today reads as a different action after the next one is removed.
    /// </remarks>
    [JsonPropertyName(AssistantEventFields.Kind)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the tool that staged it.</summary>
    [JsonPropertyName(AssistantEventFields.Tool)]
    public string? Tool { get; set; }

    /// <summary>Gets or sets the instance it would act on, when it names one.</summary>
    [JsonPropertyName(AssistantEventFields.Instance)]
    public string? Instance { get; set; }

    /// <summary>Gets or sets how long it stays redeemable, in seconds.</summary>
    [JsonPropertyName(AssistantEventFields.ExpiresInSec)]
    public long? ExpiresInSec { get; set; }
}

/// <summary>
/// Data for <c>assistant_blueprint_authoring_started</c> — an authoring run began.
/// </summary>
/// <remarks>
/// The opening bracket around an ordinary install and uninstall the engine records in full. The probe
/// name is the correlation key: it is what the engine's own rows name, and knowing it is what lets a
/// consumer fold twenty-odd of them into one run instead of reading them as a server somebody made.
/// </remarks>
public class AssistantBlueprintAuthoringStartedEventData : BlueprintEventDataBase
{
    /// <summary>Gets or sets the disposable instance the run installs to test its draft.</summary>
    [JsonPropertyName(AssistantEventFields.Probe)]
    public string Probe { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>assistant_blueprint_authored</c> — an authoring run concluded.
/// </summary>
/// <remarks>
/// The one record of how it ended. On success the engine also emits <c>blueprint_created</c>, which
/// reports a <em>file</em> appearing; this reports a <em>run</em> concluding, and on failure it is the
/// only event either way.
/// </remarks>
public class AssistantBlueprintAuthoredEventData : BlueprintEventDataBase
{
    /// <summary>Gets or sets the disposable instance the run tested with.</summary>
    [JsonPropertyName(AssistantEventFields.Probe)]
    public string Probe { get; set; } = string.Empty;

    /// <summary>Gets or sets how it ended (<c>verified</c>, <c>draft_ready</c>, <c>failed</c>).</summary>
    [JsonPropertyName(AssistantEventFields.AuthoringOutcome)]
    public string AuthoringOutcome { get; set; } = string.Empty;

    /// <summary>Gets or sets how long the run took, in seconds.</summary>
    [JsonPropertyName(AssistantEventFields.DurationSec)]
    public long? DurationSec { get; set; }
}

/// <summary>
/// Data for <c>file_written</c> — an instance's file was edited through the Control Panel's file
/// browser.
/// </summary>
/// <remarks>
/// <b>Never carries the content</b>, only what identifies the write. An instance's configuration
/// files hold rcon passwords, tokens and webhook URLs.
/// </remarks>
public class FileWrittenEventData : EventDataBase
{
    /// <summary>Gets or sets the path written, relative to the instance.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets how many bytes were written.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the content hash as <c>sha256:&lt;hex&gt;</c> — identity, never content.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// Data for <c>backup_downloaded</c> — an instance's backup archive was authorised to leave the host.
/// </summary>
/// <remarks>
/// Recorded when the bytes were released, not when somebody clicked: the fact worth keeping is that a
/// copy of a world left this machine.
/// </remarks>
public class BackupDownloadedEventData : EventDataBase
{
    /// <summary>Gets or sets the backup that was served.</summary>
    public string BackupId { get; set; } = string.Empty;

    /// <summary>Gets or sets the archive's size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the archive hash as <c>sha256:&lt;hex&gt;</c>.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// Data for <c>server.transfer.started</c> — the target's first line for a server arriving from another
/// node.
/// </summary>
/// <remarks>
/// Not instance-shaped: the server has no id on this node yet. It is named by where it came from.
/// </remarks>
public class TransferStartedEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the transfer's id, the same on both nodes.</summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>Gets or sets the source node's member id.</summary>
    public string SourceMember { get; set; } = string.Empty;

    /// <summary>Gets or sets the server's instance id on the source.</summary>
    public string SourceInstance { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>server.transfer.completed</c> — the source's last line for a server that now lives on
/// another node.
/// </summary>
public class TransferCompletedEventData : EventDataBase
{
    /// <summary>Gets or sets the transfer's id, the same on both nodes.</summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>Gets or sets the target node's member id.</summary>
    public string TargetMember { get; set; } = string.Empty;

    /// <summary>Gets or sets the instance id the target minted.</summary>
    public string TargetInstance { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>server.transfer.failed</c> — a transfer that ended without moving the server, as one node
/// observed it.
/// </summary>
/// <remarks>
/// Written by both sides: the source names its own instance as <see cref="EventDataBase.InstanceName"/>,
/// the target names the source's as <see cref="SourceInstance"/> and leaves <c>InstanceName</c> empty.
/// </remarks>
public class TransferFailedEventData : EventDataBase
{
    /// <summary>Gets or sets the transfer's id, the same on both nodes.</summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>Gets or sets the server's instance id on the source, when the target wrote the line.</summary>
    public string? SourceInstance { get; set; }

    /// <summary>Gets or sets the refusal code the transfer ended with.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets the step the transfer had reached.</summary>
    public string Phase { get; set; } = string.Empty;
}

/// <summary>
/// The base for what the reactor reports about what it judged.
/// </summary>
/// <remarks>
/// <see cref="KgsmEventDataBase"/> rather than <see cref="ServiceEventData"/>, for the same reason
/// <see cref="AssistantEventData"/> is: <b>no payload names the producer</b>. The journal directory a
/// line was read from already answers that, and a field inside the payload would be a claim a reader
/// cannot check — able, therefore, to disagree.
/// </remarks>
public abstract class ReactorEventData : KgsmEventDataBase
{
    /// <summary>Gets or sets the rule that decided.</summary>
    /// <remarks>
    /// The actor an audit row carries: <c>rule:&lt;id&gt;</c>. Nobody decided anything at three in the
    /// morning — the rule did.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Rule)]
    public string Rule { get; set; } = string.Empty;

    /// <summary>Gets or sets what was judged — a server name, a sensor reference, a component.</summary>
    [JsonPropertyName(ReactorEventFields.Subject)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets what the rule would do, as a stable name.</summary>
    /// <remarks>
    /// <c>none</c> for a rule that reports and proposes nothing, which is a real answer rather than a
    /// missing one: the decision record is that rule's whole output.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Action)]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server the action operates on, or <see langword="null"/> when it operates on
    /// none.
    /// </summary>
    /// <remarks>
    /// <b>Distinct from <see cref="Subject"/>, which is what was judged.</b> They are the same
    /// string for most rules and will not stay that way: a rule judging a host sensor can still
    /// propose something about a server, and a consumer that routed on the subject would file that
    /// under the sensor.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.ActionInstance)]
    public string? ActionInstance { get; set; }

    /// <summary>Gets or sets the decision's own identity.</summary>
    [JsonPropertyName(ReactorEventFields.DecisionId)]
    public string DecisionId { get; set; } = string.Empty;
}

/// <summary>
/// Data for <c>reactor.decided</c> — a rule reached a verdict about a subject.
/// </summary>
/// <remarks>
/// <b>A verdict, not an action.</b> In observe mode — which is where every rule starts and the most
/// the build honours today — nothing is staged and nothing is performed, so this reports what the host
/// noticed. A consumer that rendered it as something having been done would be announcing work that
/// did not happen.
/// </remarks>
public class ReactorDecidedEventData : ReactorEventData
{
    /// <summary>
    /// Gets or sets what sort of thing the subject is: <c>instance</c>, <c>host</c>, <c>leaf</c>,
    /// <c>unknown</c>.
    /// </summary>
    /// <remarks>
    /// <b>Carried rather than derived.</b> A consumer that worked it out by looking the name up and
    /// seeing what it found would be guessing — a host-scoped subject like <c>k10temp/Tctl</c> has no
    /// server to resolve to, and a router needs to know that rather than discover it by failing.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.SubjectKind)]
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>Gets or sets how loudly the rule speaks, in the ecosystem's severity spellings.</summary>
    [JsonPropertyName(ReactorEventFields.Severity)]
    public string Severity { get; set; } = string.Empty;

    /// <summary>Gets or sets the authority it ran under: <c>observe</c>, <c>propose</c>, <c>act</c>.</summary>
    /// <remarks>
    /// What was actually in force, never what the rule asked for. A rule asking for an authority the
    /// build does not honour observes, and this reports the observing.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Mode)]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Gets or sets what was decided — see <see cref="ReactorOutcomes"/>.</summary>
    [JsonPropertyName(ReactorEventFields.Outcome)]
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Gets or sets why, in one line.</summary>
    /// <remarks>
    /// <b>Always present, and the point of the whole event.</b> It carries the figures the decision
    /// rests on, which is what lets a reader see how thin the evidence was without going to look.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Reason)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets who had shaped the rule when it decided, as <c>provider:name</c>, or
    /// <see langword="null"/> when nobody is known to have.
    /// </summary>
    /// <remarks>
    /// <b>Provenance beside the actor, never instead of it.</b> The rule performed the act; a person
    /// wrote the rule. A consumer renders <em>"stopped by rule <c>disk_pressure_stop</c>, written by
    /// <c>discord:tanya</c>"</em>.
    /// </remarks>
    /// <remarks>
    /// <b>Null is a real state.</b> A rule the build ships, or one hand-written into the file over
    /// SSH, carries no identity — and there is no fallback to the OS user anywhere in this ecosystem.
    /// Render its absence rather than substituting the host, the daemon, or whoever is reading.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.RuleAuthor)]
    public string? RuleAuthor { get; set; }

    /// <summary>Gets or sets when the condition opened.</summary>
    /// <remarks>The envelope's timestamp is when it was decided; these differ by the settle window.</remarks>
    [JsonPropertyName(ReactorEventFields.OpenedAt)]
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Gets or sets whose journal the originating line is in.</summary>
    [JsonPropertyName(ReactorEventFields.SourceProducer)]
    public string SourceProducer { get; set; } = string.Empty;

    /// <summary>Gets or sets which segment file of it.</summary>
    [JsonPropertyName(ReactorEventFields.SourceSegment)]
    public string SourceSegment { get; set; } = string.Empty;

    /// <summary>Gets or sets the byte offset in that segment.</summary>
    [JsonPropertyName(ReactorEventFields.SourceOffset)]
    public long SourceOffset { get; set; }

    /// <summary>
    /// Gets or sets the id the originating line's producer minted for it, or <see langword="null"/>
    /// when that line carries none.
    /// </summary>
    /// <remarks>
    /// Beside the position rather than instead of it, and both are needed. The position <em>finds</em>
    /// the line cheaply; the id <em>proves</em> it is the right one. A consumer that follows the
    /// pointer and finds the two disagree has caught a rewritten segment, where following the position
    /// alone would hand it a real, parseable event of the wrong kind with nothing to notice.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.SourceEventId)]
    public string? SourceEventId { get; set; }
}

/// <summary>
/// Data for <c>reactor.acted</c> — a decision was carried out, however it went.
/// </summary>
/// <remarks>
/// <b>The one reactor event that is an audit row.</b> Something was performed on this host with no
/// person behind the request, which is exactly what an audit trail is for — and the actor is the rule,
/// with <see cref="ReactorDecidedEventData.RuleAuthor"/> naming whoever shaped it.
/// </remarks>
public class ReactorActedEventData : ReactorEventData
{
    /// <summary>Gets or sets whether the action succeeded.</summary>
    /// <remarks>
    /// False is a complete fact and not an absence: the decision stands, the attempt happened, and it
    /// did not work. <see cref="Detail"/> says what went wrong.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Ok)]
    public bool Ok { get; set; }

    /// <summary>
    /// Gets or sets what the action produced — a backup id — or <see langword="null"/> when it
    /// produced nothing nameable.
    /// </summary>
    [JsonPropertyName(ReactorEventFields.Artifact)]
    public string? Artifact { get; set; }

    /// <summary>Gets or sets what went wrong, or what else is worth reading.</summary>
    [JsonPropertyName(ReactorEventFields.Detail)]
    public string? Detail { get; set; }
}

/// <summary>
/// Data for <c>reactor.proposed</c> — a rule staged an action for a person to confirm.
/// </summary>
/// <remarks>
/// <b>Nothing has been done.</b> The action named here is held under <see cref="ProposalHandle"/> and will
/// stay held until somebody redeems it or <see cref="ExpiresAt"/> passes. A consumer that rendered this
/// as work performed would be announcing something that has not happened — and a consumer that renders
/// it as work <em>about to</em> happen is wrong too, because the commonest ending is that the condition
/// resolves itself and nobody ever answers.
/// </remarks>
public class ReactorProposedEventData : ReactorEventData
{
    /// <summary>Gets or sets the token this proposal is redeemed with.</summary>
    /// <remarks>
    /// <b>It is a capability, not a name.</b> Anything that can present it can ask for the proposal
    /// to be confirmed, subject to the same authority the action itself requires — so it belongs in a
    /// panel and a push payload, and not in a channel a fleet reads.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.ProposalHandle)]
    public string ProposalHandle { get; set; } = string.Empty;

    /// <summary>Gets or sets when an unanswered proposal stops being redeemable.</summary>
    [JsonPropertyName(ReactorEventFields.ExpiresAt)]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets what sort of thing the subject is: <c>instance</c>, <c>host</c>, <c>leaf</c>,
    /// <c>unknown</c>.
    /// </summary>
    [JsonPropertyName(ReactorEventFields.SubjectKind)]
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>Gets or sets how loudly the rule speaks, in the ecosystem's severity spellings.</summary>
    [JsonPropertyName(ReactorEventFields.Severity)]
    public string Severity { get; set; } = string.Empty;

    /// <summary>Gets or sets why the rule concluded this, in one line.</summary>
    /// <remarks>
    /// <b>The sentence a person decides on.</b> It carries the figures the decision rests on, and it is
    /// what a confirm dialog shows — a proposal rendered without it asks somebody to authorise an
    /// action on trust.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Reason)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets who had shaped the rule when it staged this, as <c>provider:name</c>, or
    /// <see langword="null"/> when nobody is known to have.
    /// </summary>
    [JsonPropertyName(ReactorEventFields.RuleAuthor)]
    public string? RuleAuthor { get; set; }

    /// <summary>Gets or sets when the condition opened.</summary>
    [JsonPropertyName(ReactorEventFields.OpenedAt)]
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>
/// Data for <c>reactor.resolved</c> — a staged proposal reached its end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read <see cref="Resolution"/> and <see cref="Ok"/> as two separate answers.</b> The resolution
/// is what the person did; <c>Ok</c> is what the action did, and it is <see langword="null"/> whenever
/// nothing ran — which is three of the four resolutions. A consumer treating a null as a failure would
/// report every dismissal as a broken action.
/// </para>
/// <para>
/// <b>An audit row only when a person confirmed.</b> Something was performed on this host with a name
/// behind it, and that name is <see cref="AnsweredBy"/>: the rule found the condition and offered, the
/// person authorised it. The rule is provenance here, not the actor — which is the opposite way round
/// from <c>reactor.acted</c>.
/// </para>
/// </remarks>
public class ReactorResolvedEventData : ReactorEventData
{
    /// <summary>Gets or sets the handle of the proposal that ended.</summary>
    [JsonPropertyName(ReactorEventFields.ProposalHandle)]
    public string ProposalHandle { get; set; } = string.Empty;

    /// <summary>Gets or sets how it ended — see <see cref="ReactorResolutions"/>.</summary>
    [JsonPropertyName(ReactorEventFields.Resolution)]
    public string Resolution { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets who answered, as <c>provider:name</c>, or <see langword="null"/> when nobody did.
    /// </summary>
    /// <remarks>
    /// Null is the whole content of a lapse: the offer was made, the window passed, and no person is
    /// named because none was involved. There is no fallback to the OS user or to whoever is reading.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.AnsweredBy)]
    public string? AnsweredBy { get; set; }

    /// <summary>
    /// Gets or sets whether the action succeeded, or <see langword="null"/> when none was attempted.
    /// </summary>
    [JsonPropertyName(ReactorEventFields.Ok)]
    public bool? Ok { get; set; }

    /// <summary>
    /// Gets or sets what the action produced — a backup id — or <see langword="null"/> when it
    /// produced nothing nameable.
    /// </summary>
    [JsonPropertyName(ReactorEventFields.Artifact)]
    public string? Artifact { get; set; }

    /// <summary>Gets or sets what went wrong, or what else is worth reading.</summary>
    /// <remarks>
    /// For <see cref="ReactorResolutions.NoLongerApplicable"/> this carries the rule's fresh verdict —
    /// the sentence explaining what the world says now, which is the only place a person finds out
    /// <em>why</em> the thing they just authorised did not run.
    /// </remarks>
    [JsonPropertyName(ReactorEventFields.Detail)]
    public string? Detail { get; set; }
}
