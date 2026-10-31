using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Typed client for the kgsm-watchdog control surface — the single C# entry point
/// for driving the resident supervisor daemon over its unix socket. Surfaces above
/// (the Discord bot, a future CLI/web BFF) issue lifecycle commands through this
/// instead of shelling out, keeping all watchdog integration in kgsm-lib.
/// </summary>
/// <remarks>
/// Transport is HTTP/1.1 over a unix-domain socket. The socket's filesystem
/// permissions are the security boundary; there is no in-band authentication
/// (that belongs to the network-facing surfaces above). The action methods throw
/// <see cref="System.Net.Http.HttpRequestException"/> when the daemon is
/// unreachable; <see cref="IsReadyAsync"/> instead reports <c>false</c>, so it can
/// be used as a presence probe.
/// </remarks>
public interface IWatchdogClient : IDisposable
{
    /// <summary>
    /// Probes supervisor readiness via the unified <c>GET /health</c> probe. Returns
    /// <c>true</c> only when the daemon answers 200 (in-slice and able to spawn); any
    /// other status (503 + reason) or an unreachable/stale socket yields <c>false</c>
    /// rather than throwing, so this doubles as a presence check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the supervisor readiness detail (the <c>/health</c> body, served on
    /// both 200 and 503). Returns <c>null</c> if the daemon is unreachable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogReadyState?> GetReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the daemon spawn and supervise <paramref name="instanceName"/>
    /// (records desired-state = running). An already-running instance returns a
    /// result with <see cref="WatchdogActionResult.Ok"/> = false rather than
    /// throwing. Resets the crash-recovery streak and clears the give-up latch — this is
    /// the operator-override path.
    /// </summary>
    /// <param name="instanceName">The instance to start.</param>
    /// <param name="origin">The requesting leaf, e.g. <c>"scheduler"</c> (the default).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogActionResult> StartAsync(
        string instanceName,
        string origin = "scheduler",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the daemon stop <paramref name="instanceName"/> (records
    /// desired-state = stopped, so it will not be crash-restarted) and perform the
    /// graceful drain → <c>cgroup.kill</c> teardown.
    /// </summary>
    /// <param name="instanceName">The instance to stop.</param>
    /// <param name="origin">The requesting leaf, e.g. <c>"scheduler"</c> (the default).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogActionResult> StopAsync(
        string instanceName,
        string origin = "scheduler",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="instanceName"/> to the watchdog's persisted boot-autostart set so the
    /// daemon will spawn it automatically on the next host boot (or watchdog start). Idempotent —
    /// already-enabled returns <see cref="WatchdogActionResult.Ok"/> = false (409) rather than throwing.
    /// </summary>
    Task<WatchdogActionResult> EnableAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes <paramref name="instanceName"/> from the watchdog's persisted boot-autostart set.
    /// Idempotent — already-disabled returns <see cref="WatchdogActionResult.Ok"/> = false (409) rather than throwing.
    /// </summary>
    Task<WatchdogActionResult> DisableAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the names of all instances currently in the persisted boot-autostart set.
    /// An empty list means no instances are enabled (never null).
    /// </summary>
    Task<IReadOnlyList<string>> GetEnabledNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deregisters <paramref name="instanceName"/> from the watchdog entirely — the supervision table
    /// entry, its cgroup, its boot-autostart intent, and its persisted restart counters. The counterpart
    /// to an uninstall: an instance that no longer exists must stop being supervised, or the daemon holds
    /// a <c>desired=running</c> record forever and every consumer of its state keeps seeing a condition
    /// for a server that is gone.
    /// <para>
    /// Idempotent and existence-free — an unknown name returns <see cref="WatchdogActionResult.Ok"/> =
    /// true as a no-op, since the instance's kgsm spec is normally already deleted by the time this is
    /// called. <see cref="WatchdogActionResult.Ok"/> = false (409) means the instance is still running
    /// and was NOT deregistered (deregistering it would orphan the process).
    /// </para>
    /// </summary>
    Task<WatchdogActionResult> ForgetAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Live-applies a CPU scheduling priority to the running instance's cgroup.
    /// Translates the priority string to a cgroup <c>cpu.weight</c> value
    /// (low=50, normal=100, high=400) and writes it. Returns <c>Ok=false</c>
    /// with a message (not an exception) if the instance cgroup does not exist
    /// (not running) — the caller should treat this as "will apply at next start".
    /// </summary>
    Task<WatchdogActionResult> SetCpuPriorityAsync(string instanceName, string priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the daemon atomically restart <paramref name="instanceName"/>:
    /// stops the process, waits for the cgroup to drain, then respawns it.
    /// Does not increment the crash-recovery streak (this is an intentional restart).
    /// The <paramref name="origin"/> names the REQUESTING LEAF, and the daemon attributes the
    /// emitted audit event to it — as <c>system:&lt;origin&gt;</c>, the actor form a consumer reads
    /// as an autonomous leaf rather than as a person on the local host. The event's own origin is
    /// <c>system</c>, since a leaf-driven restart has no human surface behind it.
    /// </summary>
    /// <param name="instanceName">The instance to restart.</param>
    /// <param name="origin">The requesting leaf, e.g. <c>"scheduler"</c> (the default).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogActionResult> RestartAsync(
        string instanceName,
        string origin = "scheduler",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parks <paramref name="instanceName"/>: the daemon performs the normal graceful drain, and the
    /// instance stays parked until <see cref="EndMaintenanceAsync"/> releases it or the daemon's own
    /// unpark timeout expires. This is the primitive a leaf runs a multi-minute disruptive sequence
    /// behind — an update, or a backup that must run against a stopped server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Parked is stopped, but still wanted running.</b> Desired-state stays <c>running</c>, so a
    /// parked instance is not a stopped one: crash-restart is suppressed for as long as the park
    /// holds rather than being switched off, the failure streak and the give-up latch are left
    /// exactly as they were, and the phase is persisted like every other. A stop/start pair says
    /// something different — it records that nobody wants the server up, and a leaf that dies between
    /// the two leaves it down for good.
    /// </para>
    /// <para>
    /// The work itself happens outside the park call, not inside the daemon: this returns as soon as
    /// the instance is drained. The daemon unparks on its own once the unpark timeout expires, so a
    /// leaf that dies mid-sequence costs a window, never a server.
    /// </para>
    /// </remarks>
    /// <param name="instanceName">The instance to park.</param>
    /// <param name="origin">The requesting leaf, e.g. <c>"scheduler"</c> (the default).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogActionResult> BeginMaintenanceAsync(
        string instanceName,
        string origin = "scheduler",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases <paramref name="instanceName"/> from a park and respawns it, without going through
    /// the operator-override path — so the failure streak and the give-up latch come out of the park
    /// as they went in. Idempotent: an instance that is not parked returns
    /// <see cref="WatchdogActionResult.Ok"/> = false rather than throwing.
    /// </summary>
    /// <remarks>
    /// A leaf calls this unconditionally, whatever the work it parked for did, which is what
    /// guarantees a maintenance window never leaves a server down.
    /// </remarks>
    /// <param name="instanceName">The instance to release.</param>
    /// <param name="origin">The requesting leaf, e.g. <c>"scheduler"</c> (the default).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogActionResult> EndMaintenanceAsync(
        string instanceName,
        string origin = "scheduler",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the supervised state of a single instance, or <c>null</c> when the
    /// daemon does not track it (HTTP 404).
    /// </summary>
    /// <param name="instanceName">The instance to query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogInstanceState?> GetStatusAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every instance the daemon currently supervises.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<WatchdogInstanceState>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The run clock for every instance the daemon can date — the ones it supervises right now, and the
    /// ones only its durable run ledger remembers.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="ListAsync"/> when the question is how long something has been up or
    /// down: a stopped instance is not in the supervised table, so it does not appear in a list at all.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<WatchdogRunTimes>> GetRunTimesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Follows the live console (stdout/stderr tail) of a native, supervised instance,
    /// yielding each line as the daemon appends it. The stream carries <b>only</b> lines
    /// written <em>after</em> the call connects — it does not replay history (use
    /// <see cref="GetConsoleTailAsync"/> for a backlog).
    /// </summary>
    /// <remarks>
    /// The stream is <b>unbounded and never self-completes on the daemon's own initiative</b>:
    /// it does not end on the instance stopping, on log EOF, or on a missing-on-disk log (a
    /// missing log holds the connection open, polling, until the file appears). In normal
    /// operation enumeration ends only when the caller cancels
    /// <paramref name="cancellationToken"/> (or disposes the enumerator) — at which point an
    /// <see cref="OperationCanceledException"/> may surface from the iterator. A
    /// <em>server-side</em> disconnect (the daemon stopping, or the socket dropping) appears
    /// as stream EOF and ends the sequence <b>normally</b> (no exception) — a consumer that
    /// wants to keep following must re-invoke; the sequence ending is not by itself proof the
    /// caller cancelled. An unknown / non-native / no-console instance yields an empty
    /// sequence (the daemon answers 404 before the first byte). The shared request timeout
    /// does <b>not</b> apply to this call — it streams for as long as the caller keeps the
    /// token un-cancelled and the connection stays up.
    /// </remarks>
    /// <param name="instanceName">The instance whose console to follow.</param>
    /// <param name="cancellationToken">Stops the follow when cancelled — the normal way it ends (a server-side disconnect ends it without cancellation).</param>
    /// <returns>An async sequence of console lines (newline already stripped).</returns>
    IAsyncEnumerable<string> FollowConsoleAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a finite tail of a native, supervised instance's console — the last
    /// <paramref name="lines"/> lines of its MOST RECENT run, oldest-first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unknown / non-native / no-console instance (the daemon answers 404) returns an
    /// <b>empty</b> list rather than throwing — an honest "no console" read, mirroring how
    /// <see cref="GetStatusAsync"/> degrades a 404 to null. An instance with a console but
    /// no lines also returns an empty list. The daemon clamps <paramref name="lines"/> to
    /// its own bounds (0..5000); a request transport failure still throws.
    /// </para>
    /// <para>
    /// <b>This is one run, and after a crash-restart it is the run that came after the crash.</b>
    /// The supervisor rotates the log on every fresh spawn, so a server that aborted and was
    /// restarted has its cause in the previous run and a clean boot here. Diagnosing a crash means
    /// <see cref="GetConsoleRunsAsync"/> then <see cref="GetConsoleRunTailAsync"/>, not this.
    /// </para>
    /// </remarks>
    /// <param name="instanceName">The instance whose console tail to read.</param>
    /// <param name="lines">How many trailing lines to request (the daemon clamps 0..5000).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The trailing console lines oldest-first, or an empty list when there is no console.</returns>
    Task<IReadOnlyList<string>> GetConsoleTailAsync(string instanceName, int lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the runs of a native instance's console — each stretch of stdout between a spawn and
    /// the exit after it, newest first, with when each ended.
    /// </summary>
    /// <remarks>
    /// An unknown / non-native / no-console instance returns an <b>empty</b> list, as does a native
    /// instance that has never produced output — both are honest "no runs" answers rather than
    /// errors. A daemon too old to serve the route also answers 404 and so reads as no runs; a
    /// consumer that needs to tell those apart should fall back to
    /// <see cref="GetConsoleTailAsync"/>, which every build serves.
    /// </remarks>
    /// <param name="instanceName">The instance whose runs to list.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The runs newest-first, or an empty list when there are none.</returns>
    Task<IReadOnlyList<WatchdogConsoleRun>> GetConsoleRunsAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a finite tail of ONE run of a native instance's console, oldest-first — the run at
    /// <paramref name="run"/> in the newest-first listing <see cref="GetConsoleRunsAsync"/> returns.
    /// </summary>
    /// <remarks>
    /// A run index that does not exist returns an <b>empty</b> list, the same honest "nothing to
    /// read" as an instance with no console — the index is positional and only meaningful against
    /// the listing it came from, so pick one and use it straight away rather than storing it. Run 0
    /// is the most recent, which is what <see cref="GetConsoleTailAsync"/> reads.
    /// </remarks>
    /// <param name="instanceName">The instance whose console to read.</param>
    /// <param name="lines">How many trailing lines to request (the daemon clamps 0..5000).</param>
    /// <param name="run">Newest-first run index; 0 is the most recent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>That run's trailing console lines oldest-first, or an empty list.</returns>
    Task<IReadOnlyList<string>> GetConsoleRunTailAsync(
        string instanceName, int lines, int run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a window of ONE run's console and reports the byte range it came from, so a caller can
    /// keep reading further back through a run of any length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Page on the cursor, never on a line count.</b> Pass the
    /// <see cref="WatchdogConsoleWindow.Start"/> you were given as the next call's
    /// <paramref name="endOffset"/> and you get the lines immediately before it. Asking instead for
    /// "the 500 lines before the last 200" is wrong the moment the game prints anything between the
    /// two requests, and it prints constantly — the pages overlap or skip and nothing says so.
    /// <see cref="WatchdogConsoleWindow.HasEarlier"/> is false once the run's beginning is reached.
    /// </para>
    /// <para>
    /// An unknown / non-native / no-console instance, a run index that does not exist, and a log that
    /// has not been written yet all read as an <b>empty</b> window — the same honest "nothing to read"
    /// the rest of this surface returns. A daemon too old to report the range answers the lines with
    /// no cursor, which reads as a window that begins at 0 and therefore has nothing earlier: the
    /// caller sees no "load earlier" rather than a wrong one.
    /// </para>
    /// </remarks>
    /// <param name="instanceName">The instance whose console to read.</param>
    /// <param name="lines">How many lines this window should hold (the daemon clamps 0..5000).</param>
    /// <param name="run">Newest-first run index; 0 is the most recent.</param>
    /// <param name="endOffset">Byte offset to read back from; negative means the end of the log.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The window and its byte range, or an empty window.</returns>
    Task<WatchdogConsoleWindow> GetConsoleWindowAsync(
        string instanceName, int lines, int run, long endOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the WHOLE of one run's console log as a stream — the file somebody attaches to a bug
    /// report, rather than a window of it.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned object and must dispose it; the response stays open until then.
    /// It is a stream and not a list because a log has no bound: copy it to where it is going and
    /// nothing between the daemon and that destination ever holds all of it. Read exactly
    /// <see cref="WatchdogConsoleDownload.Length"/> bytes — the game may append past that mid-copy,
    /// and the daemon committed to the length it measured when it opened the file.
    /// </remarks>
    /// <param name="instanceName">The instance whose console to read.</param>
    /// <param name="run">Newest-first run index; 0 is the most recent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The open log, or <c>null</c> when there is nothing to serve — an unknown / non-native
    /// instance, or a daemon too old to serve the route. A known instance that has never printed is
    /// an open download of length 0, which is a different fact and stays distinguishable.
    /// </returns>
    Task<WatchdogConsoleDownload?> OpenConsoleDownloadAsync(
        string instanceName, int run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches live player presence for every instance: whether the supervisor can observe each
    /// one's players, and who it currently sees connected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every instance appears, including the ones with nobody on them.</b> That is the point: a
    /// bare session list makes an absent instance ambiguous between "nobody is online" and "this
    /// game cannot report players", and a consumer rendering the first reading of the second states
    /// something the host does not know. Check
    /// <see cref="WatchdogInstancePresence.IsDetected"/> before reading an empty list as zero.
    /// </para>
    /// <para>
    /// The map reflects who is <em>currently connected</em> — not a historical roster — and it is
    /// volatile: a watchdog restart clears it and it rebuilds as the game logs the next events.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Presence keyed by instance name, or <c>null</c> when the daemon is unreachable. <b>Null is
    /// not an empty host</b> — it is the supervisor being unavailable, and a caller must report it
    /// as unknown rather than as nobody online.
    /// </returns>
    Task<IReadOnlyDictionary<string, WatchdogInstancePresence>?> GetPlayerPresenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the UPnP port-forward mappings the local IGD currently holds for
    /// <paramref name="instanceName"/> (the rows the watchdog owns, tagged with the instance name).
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> only when the <b>daemon</b> is unreachable or does not expose the route
    /// (graceful, like <see cref="GetReadyAsync"/> — it does not throw). A reachable daemon whose
    /// <b>router</b> could not be queried
    /// (no IGD, upnpc missing, or a timeout) returns a non-null result with
    /// <see cref="WatchdogUpnpList.State"/> = <c>"unavailable"</c> — distinct from a real query that
    /// found no mappings (<c>"queried"</c> with an empty list). An unavailable state is never presented
    /// as "no forwards".
    /// </remarks>
    /// <param name="instanceName">The instance whose UPnP mappings to read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<WatchdogUpnpList?> GetUpnpAsync(string instanceName, CancellationToken cancellationToken = default);
}
