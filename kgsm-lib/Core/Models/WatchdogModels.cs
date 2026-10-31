using System.Text.Json.Serialization;

namespace TheKrystalShip.KGSM.Core.Models;

// DTOs for the kgsm-watchdog control surface (HTTP/1.1 over a unix socket). These
// mirror the daemon's own contracts (kgsm-watchdog: src/Watchdog/Model/Contracts.cs)
// one-for-one. The daemon serializes camelCase; every property below carries an
// explicit [JsonPropertyName] so binding is exact regardless of any naming policy,
// and registration in KgsmJsonContext keeps deserialization reflection-free
// (Native-AOT/trim-safe).

/// <summary>
/// Result of a watchdog control action (<c>start</c>/<c>stop</c>): which instance,
/// whether it succeeded, and a human-readable reason.
/// </summary>
public record class WatchdogActionResult
{
    /// <summary>The instance the action targeted.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    /// <summary>
    /// Whether the action succeeded. False is returned (with HTTP 409) when the
    /// instance was already in the requested state, alongside an explanatory
    /// <see cref="Message"/> — it is not an exception, just a no-op outcome.
    /// </summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>Human-readable detail describing the outcome.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Which kind of refusal this is, when it is one: a value of <see cref="WatchdogRefusals"/>, null
    /// otherwise. <see cref="Message"/> is prose and free to be reworded; a caller branches on this.
    /// </summary>
    [JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Refusal { get; set; }
}

/// <summary>The refusal kinds <see cref="WatchdogActionResult.Refusal"/> can carry.</summary>
public static class WatchdogRefusals
{
    /// <summary>The node has no room for the instance to start.</summary>
    public const string NoRoom = "no_room";

    /// <summary>The instance is locked for a transfer and does not start.</summary>
    public const string InstanceLocked = "instance_locked";
}

/// <summary>
/// Reported state of a supervised instance: the <em>desired</em> state the daemon
/// holds vs. the <em>actual</em> liveness measured from <c>cgroup.events</c>, plus
/// the supervision phase and current restart-failure streak. Never fabricated —
/// <see cref="Populated"/> is read from the kernel.
/// </summary>
public record class WatchdogInstanceState
{
    /// <summary>The instance name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Desired state held by the daemon: <c>"running"</c> or <c>"stopped"</c>.</summary>
    [JsonPropertyName("desired")]
    public string Desired { get; set; } = string.Empty;

    /// <summary>Actual liveness, measured from the instance's <c>cgroup.events</c>.</summary>
    [JsonPropertyName("populated")]
    public bool Populated { get; set; }

    /// <summary>The spawned leader PID, when known; null otherwise.</summary>
    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    /// <summary>The instance's cgroup path under the delegated slice.</summary>
    [JsonPropertyName("cgroupPath")]
    public string CgroupPath { get; set; } = string.Empty;

    /// <summary>
    /// Supervision phase: <c>"running"</c>, <c>"restart-pending"</c>,
    /// <c>"stopped"</c>, <c>"maintenance"</c>, <c>"failed"</c>, or <c>"unknown"</c>.
    /// </summary>
    /// <remarks>
    /// <c>"maintenance"</c> is a server deliberately down while a leaf works on it:
    /// <see cref="Desired"/> is still <c>"running"</c>, crash-restart is suppressed for as long as the
    /// park holds, and <see cref="Restarts"/> is untouched. Rendering it as stopped or as failed says
    /// something the daemon does not: nobody asked for this server to be down, and nothing about it
    /// went wrong.
    /// </remarks>
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    /// <summary>Consecutive-failure streak since last stability (0 when healthy).</summary>
    [JsonPropertyName("restarts")]
    public int Restarts { get; set; }

    /// <summary>Last transition reason (e.g. <c>"crashed (exit 139); restart in 2s"</c>).</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// When the CURRENT run was spawned (UTC), or null when nothing is running — and when the daemon
    /// adopted a live cgroup it did not spawn, where there is no spawn time to state.
    /// <para>
    /// The daemon persists this alongside the phase, so it survives a daemon restart. That is what makes
    /// it an uptime rather than a "seen since": a redeploy of the watchdog does not reset it.
    /// </para>
    /// </summary>
    [JsonPropertyName("spawnedAt")]
    public DateTime? SpawnedAt { get; set; }

    /// <summary>
    /// When this instance's LAST run ended (UTC), read from the daemon's durable run ledger, or null
    /// when it has no recorded runs — an honest unknown, never a fabricated date.
    /// <para>
    /// This is the run's own last output (the console file's mtime), not the moment the supervisor
    /// noticed the cgroup had emptied; the two differ by up to a poll interval.
    /// </para>
    /// </summary>
    [JsonPropertyName("lastExitedAt")]
    public DateTime? LastExitedAt { get; set; }
}

/// <summary>
/// One instance's run clock: when its current run was spawned, and when its last run ended.
/// </summary>
/// <remarks>
/// Distinct from <see cref="WatchdogInstanceState"/> because it answers for instances that state cannot:
/// an instance leaves the daemon's supervised table when it stops, and "how long has this been down" is
/// asked of exactly those. Both halves are read from state the daemon persists, so both survive a daemon
/// restart.
/// </remarks>
public record class WatchdogRunTimes
{
    /// <summary>The instance name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>When the current run was spawned (UTC), or null when nothing is running.</summary>
    [JsonPropertyName("spawnedAt")]
    public DateTime? SpawnedAt { get; set; }

    /// <summary>When the last recorded run ended (UTC), or null when no run is on record.</summary>
    [JsonPropertyName("lastExitedAt")]
    public DateTime? LastExitedAt { get; set; }
}

/// <summary>
/// Readiness of the supervisor itself: whether it is in-slice and able to spawn.
/// Distinct from process liveness — the daemon can be up but unable to supervise.
/// </summary>
public record class WatchdogReadyState
{
    /// <summary>True when the supervisor is in-slice and able to spawn.</summary>
    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    /// <summary>Human-readable detail (the precise reason when not ready).</summary>
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// One UPnP port-mapping row the local IGD holds for an instance, mirroring the daemon's
/// <c>UpnpMapping</c> (kgsm-watchdog: <c>GET /upnp/{name}</c>). Measured from the router, never
/// fabricated — the mapping's <see cref="Description"/> equals the instance name (the ownership tag the
/// watchdog sets when it opens the forward).
/// </summary>
public record class WatchdogUpnpMapping
{
    /// <summary>The external (WAN-side) port the router forwards.</summary>
    [JsonPropertyName("externalPort")]
    public int ExternalPort { get; set; }

    /// <summary>Transport protocol — <c>"tcp"</c> or <c>"udp"</c>.</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    /// <summary>The internal (LAN-side) port the forward targets.</summary>
    [JsonPropertyName("internalPort")]
    public int InternalPort { get; set; }

    /// <summary>The internal (LAN-side) client address the forward targets.</summary>
    [JsonPropertyName("internalClient")]
    public string InternalClient { get; set; } = string.Empty;

    /// <summary>The mapping description — equals the owning instance's name (the ownership tag).</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// An instance's current UPnP mappings on the local IGD (kgsm-watchdog: <c>GET /upnp/{name}</c>).
/// <see cref="State"/> is load-bearing for honesty: <c>"queried"</c> means the router was asked and
/// <see cref="Mappings"/> is what it owns (possibly empty — a real "none"); <c>"unavailable"</c> means
/// the router could not be asked at all (upnpc missing, no IGD, or a timeout) and is NEVER "no mappings".
/// This in-body <c>"unavailable"</c> (the daemon is reachable, the router is not) is distinct from a null
/// return (the daemon itself unreachable).
/// </summary>
public record class WatchdogUpnpList
{
    /// <summary>The instance the mappings belong to.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    /// <summary>Query state: <c>"queried"</c> (asked the router) or <c>"unavailable"</c> (couldn't).</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>The mappings the router owns for this instance; empty when queried-and-none.</summary>
    [JsonPropertyName("mappings")]
    public List<WatchdogUpnpMapping> Mappings { get; set; } = [];
}

/// <summary>
/// A single player session tracked by the watchdog's in-memory session map. Served by
/// <c>GET /players</c> so consumers (kgsm-api) can reconcile their roster on startup.
/// </summary>
public record class WatchdogPlayer
{
    /// <summary>The session key (first non-blank of key, addr, id, name — contract §4).</summary>
    [JsonPropertyName("sessionKey")]
    public string? SessionKey { get; set; }

    /// <summary>The player's account or platform id, when known.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The player's display name, when known.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The player's network address, when known.</summary>
    [JsonPropertyName("addr")]
    public string? Addr { get; set; }
}

/// <summary>
/// One instance's player presence: whether the supervisor can observe it, and who it currently
/// sees connected. Served by <c>GET /players</c>, one entry per instance.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Detection"/> is what makes <see cref="Players"/> readable, and the pair is why this
/// is one type.</b> An empty list under <c>log</c> or <c>rcon</c> means nobody is connected — a
/// measured fact. An empty list under <c>none</c> means the game reports nothing, and a surface that
/// renders it as "0 online" states something the host does not know. A consumer cannot take the
/// list without the qualifier because they arrive together.
/// </para>
/// <para>
/// <b>The supervisor decides this, not the consumer.</b> The predicate behind it includes whether a
/// pattern <i>compiles</i>, so re-deriving it from an instance's config is something no surface can
/// get right — and three surfaces each deriving it is how they come to disagree. Use
/// <see cref="IsDetected"/> rather than comparing the string.
/// </para>
/// </remarks>
public record class WatchdogInstancePresence
{
    /// <summary>
    /// How presence is observed: <c>log</c> (matched from the game's output — real transitions),
    /// <c>rcon</c> (polled and diffed — cannot see churn between polls), <c>none</c> (not observable
    /// at all), or <c>unknown</c> (the supervisor could not read the instance inventory, so the
    /// capability could not be established either way).
    /// </summary>
    [JsonPropertyName("detection")]
    public string Detection { get; set; } = "unknown";

    /// <summary>
    /// The sessions currently tracked. <b>Empty means "nobody" only when <see cref="IsDetected"/>
    /// is true</b>; otherwise it means nobody can tell.
    /// </summary>
    [JsonPropertyName("players")]
    public List<WatchdogPlayer> Players { get; set; } = [];

    /// <summary>
    /// Whether this instance's roster is a measurement. False for both <c>none</c> and
    /// <c>unknown</c> — a capability that could not be established is not one that exists, and the
    /// honest answer to "who is online" is the same in both cases.
    /// </summary>
    [JsonIgnore]
    public bool IsDetected =>
        Detection is "log" or "rcon";
}

/// <summary>
/// One run of a native instance's console — a single stretch of stdout between a spawn and the exit
/// that followed it. Served by <c>GET /console/{name}/runs</c>, newest first.
/// </summary>
/// <remarks>
/// <para>
/// <b>The supervisor rotates the log on every fresh spawn, so a crash and the restart behind it are
/// two runs, not one.</b> Reading only the live console after a crash-restart shows a clean boot and
/// nothing of what went wrong. A consumer diagnosing a crash matches it against
/// <see cref="EndedAt"/> and reads that <see cref="Index"/>.
/// </para>
/// <para>
/// <b><see cref="Index"/> is the whole address.</b> It is positional and newest-first, so it is only
/// meaningful against the listing it came from — read the runs, pick one, use its index straight
/// away rather than storing it. No file path is exposed: the run's bytes come back from
/// <see cref="Interfaces.IWatchdogClient.GetConsoleRunTailAsync(string, int, int, System.Threading.CancellationToken)"/>, never from the caller opening anything.
/// </para>
/// </remarks>
public record class WatchdogConsoleRun
{
    /// <summary>Newest-first position; 0 is the most recent run.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Whether this run is IN PROGRESS — a process alive in the instance's cgroup writing this
    /// console right now. A stopped instance has no current run, including the one whose output is
    /// still sitting at the live path awaiting the next spawn's rotation.
    /// </summary>
    [JsonPropertyName("current")]
    public bool Current { get; set; }

    /// <summary>
    /// When the run's output stopped, UTC. Null only while <see cref="Current"/> — a run in progress
    /// has no end. Every finished run carries one, including the last run of a stopped instance,
    /// which is what lets a crash with no restart behind it still be found by its end time.
    /// </summary>
    [JsonPropertyName("endedAt")]
    public DateTime? EndedAt { get; set; }

    /// <summary>When the run last printed, UTC. Measured for every run, current or not.</summary>
    [JsonPropertyName("lastOutputAt")]
    public DateTime LastOutputAt { get; set; }

    /// <summary>The run's console size on disk, in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// How the run ended, as the supervisor classified it: <c>crashed</c>, <c>gave-up</c>,
    /// <c>exited</c>, <c>stopped</c>, <c>running</c> for the run in progress, or <c>unknown</c>.
    /// <para>
    /// <b><c>unknown</c> is an absence of knowledge, not a clean ending.</b> It is what a run whose
    /// end the supervisor never recorded reports — one that predates the ledger, or that ended while
    /// the daemon was down. Presenting it as "nothing went wrong" invents the fact the field exists
    /// to carry.
    /// </para>
    /// <para>
    /// This is what a consumer diagnosing a crash matches on. Matching on <see cref="EndedAt"/>
    /// alone can only ask which run stopped printing nearest the crash; this says which run the
    /// supervisor itself saw crash, and tells a crash apart from a deliberate stop — a distinction
    /// no amount of timestamp comparison recovers.
    /// </para>
    /// </summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "unknown";

    /// <summary>
    /// The exit code the supervisor read from the run's leader. Null where it could not be read, and
    /// for the run in progress, which has not exited. Games exit 0 on a fatal error often enough that
    /// this is evidence, never a verdict.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }
}

/// <summary>
/// One window of an instance's console: the lines, and the byte range of the run's log they came
/// from. Returned by <see cref="Interfaces.IWatchdogClient.GetConsoleWindowAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Start"/> is the cursor for reading further back.</b> Ask for the window ending at
/// the <see cref="Start"/> you were given and you get the lines immediately before these — exactly,
/// while the game keeps appending to the far end of the same file. A line count from the end cannot
/// do that: it names a different line on every request, so consecutive pages silently overlap or
/// skip. <see cref="Start"/> of 0 means the run begins here and there is nothing earlier to read.
/// </para>
/// <para>
/// Not JSON — the daemon serves console output as raw text and reports the range in response
/// headers, so this type is assembled by the client and needs no <c>KgsmJsonContext</c> entry.
/// </para>
/// </remarks>
/// <param name="Lines">The window's lines, oldest-first.</param>
/// <param name="Start">Byte offset of the first line — the cursor to page back with.</param>
/// <param name="End">Byte offset just past the last line.</param>
public readonly record struct WatchdogConsoleWindow(IReadOnlyList<string> Lines, long Start, long End)
{
    /// <summary>Nothing to read: no console, no such run, or a log that does not exist yet.</summary>
    public static WatchdogConsoleWindow Empty { get; } = new([], 0, 0);

    /// <summary>Whether anything precedes this window in the run's log.</summary>
    public bool HasEarlier => Start > 0;
}

/// <summary>
/// An open read over the WHOLE of one run's console log, plus the length being served. The caller
/// owns it and must dispose it — the underlying response is held open until then.
/// </summary>
/// <remarks>
/// A whole log is a stream and not a list on purpose: it is unbounded in a way a window is not, and
/// the point of this call is to hand it to something that writes it somewhere (a file, an HTTP
/// response) without any layer between the daemon and that destination holding all of it. Read
/// exactly <see cref="Length"/> bytes; the game may append past that while the copy is in flight.
/// </remarks>
public sealed class WatchdogConsoleDownload(Stream content, long length, IDisposable owner) : IDisposable
{
    /// <summary>The log's bytes, from the start of the run.</summary>
    public Stream Content { get; } = content;

    /// <summary>How many bytes the daemon committed to, measured when it opened the file.</summary>
    public long Length { get; } = length;

    /// <summary>Releases the stream and the response holding it open.</summary>
    public void Dispose()
    {
        Content.Dispose();
        owner.Dispose();
    }
}
