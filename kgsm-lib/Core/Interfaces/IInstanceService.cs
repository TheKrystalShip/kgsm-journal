using TheKrystalShip.KGSM.ComponentConfig;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Core.Models.Enums;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Interface for managing instances in KGSM.
/// </summary>
/// <remarks>
/// Failure-channel convention: a method returning <see cref="KgsmResult"/> reports a
/// failed command in the result itself (<see cref="KgsmResult.IsSuccess"/> is false) and
/// does not throw on a non-zero exit; a method returning a nullable type returns null on
/// failure. Argument validation (e.g. a null instance name) always throws, regardless of
/// return type.
/// </remarks>
public interface IInstanceService
{
    /// <summary>
    /// Gets a dictionary of all instances.
    /// </summary>
    /// <returns>A dictionary of instance names to instance objects.</returns>
    [Performs(KgsmActions.ServerRead)]
    Dictionary<string, Instance> GetAll();

    /// <summary>
    /// Reads the full instance roster, <b>distinguishing a failed read from a genuinely empty one</b>.
    /// KGSM emits <c>{}</c> for an empty roster (a successful read of zero instances), so a successful
    /// command always deserializes to a dictionary (possibly empty); only a failed command (non-zero
    /// exit) or unparseable output yields <see langword="null"/> — the nullable-return failure
    /// convention. Use this instead of <see cref="GetAll"/> on any surface that must not treat a
    /// transient engine-read failure as "zero instances" (e.g. one that would otherwise drop every
    /// server from a list or push a removal tombstone for each). <see cref="GetAll"/> collapses both
    /// cases to an empty dictionary and so cannot tell them apart.
    /// </summary>
    /// <returns>The instance roster on a successful read (may be empty), or <see langword="null"/> on a read failure.</returns>
    [Performs(KgsmActions.ServerRead)]
    Dictionary<string, Instance>? GetAllOrNull();

    /// <summary>
    /// Gets detailed information about a specific instance in JSON format.
    /// </summary>
    /// <param name="instanceName">Instance name to get information for.</param>
    /// <returns>The instance information as a structured object.</returns>
    [Performs(KgsmActions.ServerRead)]
    Instance? GetInstanceInfo(string instanceName);

    /// <summary>
    /// Gets the runtime status summary for a specific instance in JSON format.
    /// </summary>
    /// <param name="instanceName">Instance name to get status for.</param>
    /// <returns>The instance status as a structured object.</returns>
    [Performs(KgsmActions.ServerRead)]
    InstanceRuntimeStatus? GetInstanceStatus(string instanceName);

    /// <summary>
    /// Gets the runtime status of every instance in a single KGSM invocation.
    /// </summary>
    /// <param name="fast">
    /// When true, passes <c>--fast</c> so KGSM skips the per-instance network
    /// update-check (roughly a 20x speed-up on the fleet). In fast mode each
    /// instance's <see cref="VersionInfo.Latest"/> and
    /// <see cref="VersionInfo.UpdatesAvailable"/> are null and
    /// <see cref="VersionInfo.Checked"/> is false — KGSM reports "unchecked"
    /// rather than fabricating an answer.
    /// </param>
    /// <returns>
    /// A map of instance name to a <see cref="Reading{T}"/> of its runtime
    /// status. A successfully-read instance is <see cref="ReadingState.Measured"/>
    /// with its status in <see cref="Reading{T}.Value"/>; an instance whose status
    /// could not be read (e.g. a management file that cannot answer
    /// <c>--status</c>) is <see cref="ReadingState.Unavailable"/> with a
    /// <see cref="ReadingCode"/> (typically <see cref="ReadingCode.RequiresRegeneration"/>),
    /// so one bad instance never sinks the whole call. Empty if the command itself
    /// fails.
    /// </returns>
    /// <remarks>
    /// This is the bulk counterpart to <see cref="GetInstanceStatus(string)"/>:
    /// it bootstraps KGSM once instead of once per instance, which is the
    /// intended replacement for fanning a per-instance status loop out across
    /// the fleet. The <see cref="InstanceRuntimeStatus.Status"/> here is the
    /// instance management script's own status report; dedicated liveness
    /// routing (systemd vs standalone) is a separate lifecycle-layer concern.
    /// </remarks>
    [Performs(KgsmActions.ServerRead)]
    Dictionary<string, Reading<InstanceRuntimeStatus>> GetAllStatuses(bool fast = false);

    /// <summary>
    /// Installs an instance of a blueprint.
    /// </summary>
    /// <param name="blueprintName">Name of the blueprint to install.</param>
    /// <param name="library">Optional library to place the instance in, by name. Null lets KGSM
    /// resolve it: the configured <c>default_library</c>, else the sole registered library, else
    /// an error. Every instance lives in a library — there is no path escape.</param>
    /// <param name="version">Optional version to install.</param>
    /// <param name="displayName">Optional human-readable label for the instance, passed as
    /// <c>--name</c>. Free text — it decorates, and never becomes part of a path or an identifier.
    /// Null leaves the label reading as the id.</param>
    /// <param name="actor">Optional audit principal (who) propagated to KGSM as <c>KGSM_EVENT_ACTOR</c>
    /// so the emitted event is attributable, written <c>provider:name</c>; null/empty = no actor
    /// emitted (never a fabricated one).</param>
    /// <param name="origin">Optional driving surface (through-what) propagated as <c>KGSM_EVENT_ORIGIN</c>;
    /// null/empty = no surface emitted.</param>
    /// <param name="port">Optional override for the blueprint's primary game port (1-65535), passed to KGSM
    /// as <c>--port</c>. Null = keep the blueprint's declared port(s). Trailing-optional (after provenance) so
    /// existing positional callers are unaffected.</param>
    /// <param name="start">If <c>true</c>, start the server immediately after install (one-shot, not
    /// watchdog boot-autostart).</param>
    /// <param name="id">Optional explicit instance id, passed as <c>--id</c>. Null lets KGSM generate
    /// one from the blueprint name, which is what a human-facing surface wants; a caller that has to
    /// know the id before the install finishes (a job keyed on it, a test asserting on it) names it
    /// here. The engine validates it against <c>^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$</c> and against the
    /// existing roster, and refuses the install rather than adjusting what it was given.</param>
    /// <returns>Result of the instance installation operation.</returns>
    /// <remarks>
    /// The id and the label are two arguments because they are two things: the id is the durable key
    /// every path, event and downstream store uses, and the label is decoration a person changes
    /// whenever they like. <see cref="GenerateId"/> answers what an id would be without installing.
    /// </remarks>
    [Performs(KgsmActions.ServerInstall)]
    KgsmResult Install(string blueprintName, string? library = null, string? version = null, string? displayName = null, string? actor = null, string? origin = null, int? port = null, bool? start = null, string? id = null);

    /// <summary>
    /// Uninstalls an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to uninstall.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the uninstallation operation.</returns>
    [Performs(KgsmActions.ServerUninstall)]
    KgsmResult Uninstall(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Moves a stopped instance's files into another library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Minutes of copying on a large world, not a request: the engine takes a backup, copies the
    /// tree, rewrites every path the instance holds, re-points its registry entry and starts it once
    /// on the new path to confirm it runs there before removing the old tree. A caller that blocks a
    /// request thread on this holds it for the whole of that — drive it as a job.
    /// </para>
    /// <para>
    /// The verification start emits <c>instance_started</c> and <c>instance_stopped</c> partway
    /// through, with no bracket around them. A surface that reads run-state off those alone shows
    /// the server running mid-move; the move's own bracket is the caller's to keep.
    /// </para>
    /// <para>
    /// Refused unless the instance is stopped and both libraries are reachable. The engine states
    /// which of those blocked it, and the refusal text is what a surface shows.
    /// </para>
    /// </remarks>
    /// <param name="instanceName">Instance to move.</param>
    /// <param name="library">Name of the library to move it into.</param>
    /// <param name="skipSpaceCheck">
    /// Move even when the target library has less free space than the instance currently occupies.
    /// The engine still measures and prints the shortfall.
    /// </param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the move operation.</returns>
    [Performs(KgsmActions.ServerMove)]
    KgsmResult Move(string instanceName, string library, bool skipSpaceCheck = false, string? actor = null, string? origin = null);

    // ── Transfer between nodes ────────────────────────────────────────────────
    //
    // The source holds a lock token for the whole transfer: ExportManifest takes the lock, every part
    // and the commit name it, Unlock releases it. The target stages: ImportBegin, one ImportPartAsync
    // per part, ImportFinish; ImportAbort undoes a staged import. Refusals are the engine's, in its
    // exit codes (TransferExitCodes) and, for an import, in the verdict's codes (ImportRefusalCodes).

    /// <summary>
    /// Reads an instance's manifest and, with <paramref name="lockToken"/>, takes the transfer lock first
    /// (or confirms it already holds that token). The lock disables the instance's autostart and makes
    /// the watchdog refuse to start it.
    /// </summary>
    /// <param name="instanceName">Instance to export.</param>
    /// <param name="lockToken">The caller's lock token; null reads the manifest without locking.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>The manifest, or the refusal: <see cref="TransferExitCodes.InstanceLocked"/> when another token holds the lock, <see cref="TransferExitCodes.NotFound"/> for an unknown instance, and the engine's codes for a running instance or an offline library.</returns>
    /// <exception cref="TheKrystalShip.KGSM.Exceptions.KgsmException">The engine succeeded but printed a manifest this library cannot read.</exception>
    [Performs(KgsmActions.ServerExport)]
    TransferOutcome<TransferManifest> ExportManifest(string instanceName, string? lockToken = null, string? actor = null, string? origin = null);

    /// <summary>
    /// Streams one part of a locked instance into <paramref name="destination"/>: the tree as a POSIX
    /// tar, an image as <c>docker save</c> writes it. Nothing is buffered whole.
    /// </summary>
    /// <param name="instanceName">Instance being exported.</param>
    /// <param name="partId">A part id from the manifest.</param>
    /// <param name="lockToken">The token that holds the lock.</param>
    /// <param name="destination">Receives the bytes. They are data only when the result succeeded.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <param name="cancellationToken">Cancelling kills the engine process and throws <see cref="OperationCanceledException"/>.</param>
    [Performs(KgsmActions.ServerExport)]
    Task<StreamResult> ExportPartAsync(string instanceName, string partId, string lockToken, Stream destination, string? actor = null, string? origin = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the exported instance as an uninstall does and emits <c>server.exported</c>. Called after
    /// the target has finished the import.
    /// </summary>
    /// <param name="instanceName">Instance to remove.</param>
    /// <param name="lockToken">The token that holds the lock.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    [Performs(KgsmActions.ServerExport)]
    KgsmResult CommitExport(string instanceName, string lockToken, string? actor = null, string? origin = null);

    /// <summary>
    /// Releases the transfer lock and restores the autostart flag. Unlocking an unlocked instance succeeds.
    /// </summary>
    /// <param name="instanceName">Instance to unlock.</param>
    /// <param name="lockToken">The token that holds the lock; required unless <paramref name="force"/>.</param>
    /// <param name="force">Release the lock whatever token holds it.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <exception cref="ArgumentException">Neither a token nor <paramref name="force"/> was given.</exception>
    [Performs(KgsmActions.ServerExport)]
    KgsmResult Unlock(string instanceName, string? lockToken = null, bool force = false, string? actor = null, string? origin = null);

    /// <summary>
    /// The target's preflight for a manifest: every refusal at once, nothing written.
    /// </summary>
    /// <param name="manifest">The source's manifest.</param>
    /// <param name="library">Target library; null takes the engine's default.</param>
    /// <param name="blueprintSuffix">Label for a travelling custom blueprint that collides with a different one of the same name.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>The verdict, read from the engine's output whether it exited 0 or not.</returns>
    /// <exception cref="TheKrystalShip.KGSM.Exceptions.KgsmException">The engine printed no verdict.</exception>
    [Performs(KgsmActions.ServerImport)]
    ImportVerdict ImportCheck(TransferManifest manifest, string? library = null, string? blueprintSuffix = null, string? actor = null, string? origin = null);

    /// <summary>
    /// Runs the preflight and, when it passes, stages an import.
    /// </summary>
    /// <param name="manifest">The source's manifest.</param>
    /// <param name="library">Target library; null takes the engine's default.</param>
    /// <param name="blueprintSuffix">See <see cref="ImportCheck"/>.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>The import id, or the verdict that refused it.</returns>
    /// <exception cref="TheKrystalShip.KGSM.Exceptions.KgsmException">The engine printed neither an import id nor a verdict.</exception>
    [Performs(KgsmActions.ServerImport)]
    ImportBeginResult ImportBegin(TransferManifest manifest, string? library = null, string? blueprintSuffix = null, string? actor = null, string? origin = null);

    /// <summary>
    /// Streams one part from <paramref name="source"/> into a staged import. A part sent again restarts
    /// from nothing. A failed or cancelled part leaves the staging for <see cref="ImportAbort"/>.
    /// </summary>
    /// <param name="importId">The id from <see cref="ImportBegin"/>.</param>
    /// <param name="partId">A part id from the manifest.</param>
    /// <param name="source">The part's bytes.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <param name="cancellationToken">Cancelling kills the engine process and throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>Exit <see cref="TransferExitCodes.TransferIntegrity"/> for a tar that cannot be extracted or an image id that differs, <see cref="TransferExitCodes.ImportNotFound"/> for an unknown import, <see cref="TransferExitCodes.NotFound"/> for a part the manifest does not list.</returns>
    [Performs(KgsmActions.ServerImport)]
    Task<StreamResult> ImportPartAsync(string importId, string partId, Stream source, string? actor = null, string? origin = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies every part arrived, installs the instance and starts it once when it had run, emitting
    /// <c>server.imported</c>. A failure inside undoes what it did and leaves the staging for abort.
    /// </summary>
    /// <param name="importId">The id from <see cref="ImportBegin"/>.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <exception cref="TheKrystalShip.KGSM.Exceptions.KgsmException">The engine succeeded but printed an answer this library cannot read.</exception>
    [Performs(KgsmActions.ServerImport)]
    TransferOutcome<ImportedInstance> ImportFinish(string importId, string? actor = null, string? origin = null);

    /// <summary>
    /// Removes a staged import and every image it loaded that the host did not hold before. An unknown id succeeds.
    /// </summary>
    /// <param name="importId">The import to abort.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    [Performs(KgsmActions.ServerImport)]
    KgsmResult ImportAbort(string importId, string? actor = null, string? origin = null);

    /// <summary>Lists every staged import on this node.</summary>
    /// <exception cref="TheKrystalShip.KGSM.Exceptions.KgsmException">The engine succeeded but printed an answer this library cannot read.</exception>
    [Performs(KgsmActions.ServerImport)]
    TransferOutcome<List<StagedImport>> ImportList();

    /// <summary>
    /// Gets the logs for an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get logs for.</param>
    /// <param name="maxLines">Maximum number of log lines to retrieve. Default is 10.</param>
    /// <returns>Result containing the instance logs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the logs cannot be retrieved.</exception>
    [Performs(KgsmActions.ServerConsoleRead)]
    ICollection<string> GetLogs(string instanceName, int maxLines = 10);

    /// <summary>
    /// Gets the logs for an instance asynchronously.
    /// </summary>
    /// <param name="instanceName">Instance name to get logs for.</param>
    /// <param name="maxLines">Maximum number of log lines to retrieve. Default is 10.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>Result containing the instance logs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the logs cannot be retrieved.</exception>
    [Performs(KgsmActions.ServerConsoleRead)]
    Task<ICollection<string>> GetLogsAsync(string instanceName, int maxLines = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get status for.</param>
    /// <returns>Result containing the instance status.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerRead)]
    KgsmResult GetStatus(string instanceName);

    /// <summary>
    /// Gets information about an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get information for.</param>
    /// <returns>Result containing the instance information.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerRead)]
    KgsmResult GetInfo(string instanceName);

    /// <summary>
    /// Checks if an instance is currently active/running.
    /// </summary>
    /// <param name="instanceName">Instance name to check.</param>
    /// <returns>True if the instance is active, false otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerRead)]
    bool IsActive(string instanceName);

    /// <summary>
    /// Starts an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to start.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>. Forwarded to the
    /// lifecycle layer so the emitted event is attributable.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the start operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerStart)]
    KgsmResult Start(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Stops an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to stop.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the stop operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerStop)]
    KgsmResult Stop(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Restarts an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to restart.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the restart operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    [Performs(KgsmActions.ServerRestart)]
    KgsmResult Restart(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Gets the installed version of an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get version for.</param>
    /// <returns>Result containing the installed version.</returns>
    [Performs(KgsmActions.ServerRead)]
    KgsmResult GetInstalledVersion(string instanceName);

    /// <summary>
    /// Gets the latest available version for an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get latest version for.</param>
    /// <returns>Result containing the latest version.</returns>
    [Performs(KgsmActions.ServerRead)]
    KgsmResult GetLatestVersion(string instanceName);

    /// <summary>
    /// Checks if there's an update available for an instance. This reaches the game's upstream
    /// (SteamCMD, a registry, a vendor API) and takes seconds, not milliseconds.
    /// </summary>
    /// <param name="instanceName">Instance name to check for updates.</param>
    /// <param name="emit">
    /// When <see langword="true"/>, the engine records what it found beside the instance and emits
    /// <c>instance_update_available</c> for a version it has not announced before. Recording is what
    /// makes a repeated sweep silent, so exactly one caller per host should own this — the scheduler.
    /// Left <see langword="false"/>, the check answers the caller and changes nothing.
    /// </param>
    /// <param name="actor">
    /// Optional audit principal — see <see cref="Install"/>. It stamps the <c>instance_update_available</c>
    /// event <paramref name="emit"/> produces; without it the event records unclaimed, which is what a
    /// sweep nobody asked for is.
    /// </param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result indicating if an update is available.</returns>
    [Performs(KgsmActions.ServerRead)]
    KgsmResult CheckUpdate(string instanceName, bool emit = false, string? actor = null, string? origin = null);

    /// <summary>
    /// Updates an instance to the latest version.
    /// </summary>
    /// <param name="instanceName">Instance name to update.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the update operation.</returns>
    [Performs(KgsmActions.ServerUpdate)]
    KgsmResult Update(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Gets a list of backups for an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to get backups for.</param>
    /// <returns>Result whose stdout is one backup id per line, newest first.</returns>
    [Performs(KgsmActions.ServerBackupsRead)]
    KgsmResult GetBackups(string instanceName);

    /// <summary>
    /// Gets an instance's backups with the full detail each one records — creation time, captured
    /// version, size, and which directories it holds — newest first.
    /// </summary>
    /// <param name="instanceName">Instance name to get backups for.</param>
    /// <returns>
    /// The backups, or an empty list when the instance has none. Also empty when the engine call
    /// fails or its output cannot be parsed: callers must not read an empty list as proof that no
    /// backups exist — use <see cref="GetBackups"/> when the distinction matters.
    /// </returns>
    [Performs(KgsmActions.ServerBackupsRead)]
    List<InstanceBackup> GetBackupsDetailed(string instanceName);

    /// <summary>
    /// Creates a backup for an instance.
    /// </summary>
    /// <remarks>
    /// State the <paramref name="reason"/> whenever the caller knows it. It is written into the
    /// backup's manifest as a fact and never edited afterwards, and it is the only thing that tells
    /// a routine archive apart from one taken over a broken server — which is what makes "restore
    /// the latest" safe or dangerous. A caller that states nothing produces a
    /// <see cref="BackupReason.Manual"/> backup.
    /// </remarks>
    /// <param name="instanceName">Instance name to create backup for.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <param name="reason">Why this backup is being taken — one of <see cref="BackupReason"/>.</param>
    /// <param name="retention">
    /// Whether rotation may take it — one of <see cref="BackupRetention"/>. Absent ⇒ prunable, which
    /// is right for nearly everything: pinning is what an incident archive wants, and it is a
    /// separate decision from why the backup exists.
    /// </param>
    /// <returns>Result of the backup creation operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="reason"/> or <paramref name="retention"/> is not one the engine
    /// accepts — refused here rather than after minutes of archiving.
    /// </exception>
    [Performs(KgsmActions.ServerBackupsCreate)]
    KgsmResult CreateBackup(string instanceName, string? actor = null, string? origin = null,
        string? reason = null, string? retention = null);

    /// <summary>
    /// Pins one of an instance's backups: <c>prune-backups</c> skips it, and it does not count
    /// toward the sweep's keep window.
    /// </summary>
    /// <remarks>
    /// Reversible — see <see cref="UnpinBackup"/>, which is what an incident archive wants once its
    /// triage is done. It is not a delete guard: <see cref="DeleteBackup"/> removes a pinned backup
    /// like any other. Why the backup was taken is a separate fact and neither verb touches it.
    /// </remarks>
    /// <param name="instanceName">The instance whose backup to pin.</param>
    /// <param name="backupName">The id of the backup to pin.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the pin operation.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or whitespace.</exception>
    [Performs(KgsmActions.ServerBackupsManage)]
    KgsmResult PinBackup(string instanceName, string backupName, string? actor = null, string? origin = null);

    /// <summary>
    /// Hands a pinned backup back to the rotation: <c>prune-backups</c> may delete it again once it
    /// falls outside the keep window.
    /// </summary>
    /// <param name="instanceName">The instance whose backup to unpin.</param>
    /// <param name="backupName">The id of the backup to unpin.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the unpin operation.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or whitespace.</exception>
    [Performs(KgsmActions.ServerBackupsManage)]
    KgsmResult UnpinBackup(string instanceName, string backupName, string? actor = null, string? origin = null);

    /// <summary>
    /// Prunes old backups for <paramref name="instanceName"/>, keeping the
    /// <paramref name="keepN"/> most-recent entries and deleting the rest.
    /// Safe to call when there are fewer than <paramref name="keepN"/> backups
    /// (exits immediately with success). Requires the instance to have a
    /// configured backups directory.
    /// </summary>
    /// <param name="instanceName">The instance whose backups to prune.</param>
    /// <param name="keepN">
    /// Number of most-recent <b>prunable</b> backups to keep (must be ≥ 1). Pinned backups are
    /// skipped and do not consume a slot, so the window holds this many live backups however many
    /// are pinned.
    /// </param>
    /// <param name="actor">Optional audit actor label (e.g. "scheduler").</param>
    /// <param name="origin">Optional audit origin label (e.g. "scheduler").</param>
    /// <returns>Result of the prune operation.</returns>
    [Performs(KgsmActions.ServerBackupsManage)]
    KgsmResult PruneBackups(string instanceName, int keepN, string? actor = null, string? origin = null);

    /// <summary>
    /// Deletes one of an instance's backups by id. There is no undo — the backup and its manifest
    /// are removed from the backups store.
    /// </summary>
    /// <remarks>
    /// Only an id the engine itself lists as a backup is accepted; a directory in the backups store
    /// carrying no manifest is not a backup and is refused rather than removed. An unknown id fails
    /// with the engine's own error rather than succeeding silently.
    /// </remarks>
    /// <param name="instanceName">The instance whose backup to delete.</param>
    /// <param name="backupName">The id of the backup to delete.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the delete operation.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or whitespace.</exception>
    [Performs(KgsmActions.ServerBackupsManage)]
    KgsmResult DeleteBackup(string instanceName, string backupName, string? actor = null, string? origin = null);

    /// <summary>
    /// Restores a backup for an instance.
    /// </summary>
    /// <param name="instanceName">Instance name to restore backup for.</param>
    /// <param name="backupName">Name of the backup to restore.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the backup restoration operation.</returns>
    [Performs(KgsmActions.ServerBackupsRestore)]
    KgsmResult RestoreBackup(string instanceName, string backupName, string? actor = null, string? origin = null);

    /// <summary>
    /// Generates a unique instance id for a blueprint: the blueprint's own name when no instance
    /// carries it, otherwise that name with a random numeric suffix.
    /// </summary>
    /// <param name="blueprintName">The blueprint to generate an id for.</param>
    /// <param name="id">Optional id to check instead of generating one. It is echoed back when it is
    /// well-formed and free, and refused otherwise — so a surface can offer a caller's id through the
    /// same call that would otherwise mint one, and learn before installing whether it is usable.</param>
    /// <returns>Result whose standard output is the id, with no trailing decoration.</returns>
    /// <exception cref="ArgumentException">Thrown when blueprintName is null or whitespace.</exception>
    [Performs(KgsmActions.ServerInstall)]
    KgsmResult GenerateId(string blueprintName, string? id = null);

    /// <summary>
    /// Sends a save command to a running instance.
    /// Delegates to the instance's management file save command.
    /// </summary>
    /// <param name="instanceName">The instance to save.</param>
    /// <returns>Result of the save operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName is null or whitespace.</exception>
    [Performs(KgsmActions.ServerConsoleWrite)]
    KgsmResult Save(string instanceName);

    /// <summary>
    /// Sends a console command to a running instance.
    /// The command is sent to the instance's console and the last log lines are returned.
    /// </summary>
    /// <param name="instanceName">The instance to send the command to.</param>
    /// <param name="command">The console command to send.</param>
    /// <param name="actor">Optional audit principal (who) to stamp on the emitted
    /// <c>instance_input_sent</c> event via <c>KGSM_EVENT_ACTOR</c> as <c>provider:name</c>; null
    /// omits it, and the event records with no actor (never a fabricated identity).</param>
    /// <param name="origin">Optional driving surface (through which) to stamp via
    /// <c>KGSM_EVENT_ORIGIN</c>; null omits it (no surface — never fabricated).</param>
    /// <returns>Result containing log output after command execution.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or command is null or whitespace.</exception>
    [Performs(KgsmActions.ServerConsoleWrite)]
    KgsmResult SendInput(string instanceName, string command, string? actor = null, string? origin = null);

    /// <summary>
    /// Announces a message to everyone connected to a running instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine substitutes <paramref name="message"/> into the game's
    /// blueprint-declared <c>broadcast_command</c> template and sends the result to
    /// the console; this method never builds the console command itself. Check
    /// <see cref="BroadcastCommand.IsSupported"/> against the instance's
    /// <see cref="Instance.BroadcastCommand"/> to know whether the action is available
    /// before offering it — a game that declares no template refuses rather than
    /// sending a different command, and so does an instance that is not running (a
    /// write into a stopped instance's console reaches nobody).
    /// </para>
    /// <para>
    /// <b>The message is prose and carries punctuation freely.</b> The one thing it may
    /// not contain is a line break: a console reads one command per line, so a second
    /// line would deliver a command nobody issued.
    /// </para>
    /// <para>
    /// <b>Success means the engine wrote to the console, not that a person read it.</b>
    /// Nothing above this can observe delivery, so a caller must not report the message
    /// as seen.
    /// </para>
    /// </remarks>
    /// <param name="instanceName">The instance to announce on.</param>
    /// <param name="message">The text to show players.</param>
    /// <param name="actor">Optional audit principal (who) to stamp on the emitted
    /// <c>instance_announcement_sent</c> event via <c>KGSM_EVENT_ACTOR</c> as <c>provider:name</c>;
    /// null omits it, and the event records with no actor (never a fabricated identity).</param>
    /// <param name="origin">Optional driving surface (through which) to stamp via
    /// <c>KGSM_EVENT_ORIGIN</c>; null omits it (no surface — never fabricated).</param>
    /// <returns>Result of the announce operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or message is null or
    /// whitespace, or when message contains a line break.</exception>
    [Performs(KgsmActions.ServerAnnounce)]
    KgsmResult Announce(string instanceName, string message, string? actor = null, string? origin = null);

    /// <summary>
    /// Disconnects a player from a running instance.
    /// </summary>
    /// <remarks>
    /// <paramref name="target"/> must be the identity the game's <c>kick_command</c>
    /// template asks for — resolve it with
    /// <see cref="ModerationCommand.TryGetTargetKind"/> against the instance's
    /// <see cref="Instance.KickCommand"/> and read that field off the player record.
    /// The engine substitutes it into the template; this method never builds the
    /// console command itself. A game that declares no kick command refuses the call
    /// rather than sending a different one, and an instance that is not running
    /// refuses it too (a write into a stopped instance's console reaches nobody).
    /// </remarks>
    /// <param name="instanceName">The instance to moderate on.</param>
    /// <param name="target">The player identity the game addresses.</param>
    /// <param name="actor">Optional audit principal (who) to stamp on the emitted
    /// <c>instance_player_kicked</c> event via <c>KGSM_EVENT_ACTOR</c> as <c>provider:name</c>; null
    /// omits it, and the event records with no actor (never a fabricated identity).</param>
    /// <param name="origin">Optional driving surface (through which) to stamp via
    /// <c>KGSM_EVENT_ORIGIN</c>; null omits it (no surface — never fabricated).</param>
    /// <returns>Result of the kick operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or target is null or
    /// whitespace, or when target contains a line break.</exception>
    [Performs(KgsmActions.ServerPlayersKick)]
    KgsmResult Kick(string instanceName, string target, string? actor = null, string? origin = null);

    /// <summary>
    /// Disconnects a player from a running instance and blocks them from reconnecting.
    /// </summary>
    /// <remarks>
    /// Same target contract as <see cref="Kick"/>, read from the instance's
    /// <see cref="Instance.BanCommand"/>.
    /// </remarks>
    /// <param name="instanceName">The instance to moderate on.</param>
    /// <param name="target">The player identity the game addresses.</param>
    /// <param name="actor">Optional audit principal stamped on the emitted
    /// <c>instance_player_banned</c> event; null omits it.</param>
    /// <param name="origin">Optional driving surface; null omits it.</param>
    /// <returns>Result of the ban operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or target is null or
    /// whitespace, or when target contains a line break.</exception>
    [Performs(KgsmActions.ServerPlayersBan)]
    KgsmResult Ban(string instanceName, string target, string? actor = null, string? origin = null);

    /// <summary>
    /// Lifts a block, allowing a player to connect again.
    /// </summary>
    /// <remarks>
    /// Same target contract as <see cref="Kick"/>, read from the instance's
    /// <see cref="Instance.UnbanCommand"/>. Note that an unban's subject is by
    /// definition not connected, so it cannot be resolved from a live player roster —
    /// the caller supplies it from its own record of who was banned.
    /// </remarks>
    /// <param name="instanceName">The instance to moderate on.</param>
    /// <param name="target">The player identity the game addresses.</param>
    /// <param name="actor">Optional audit principal stamped on the emitted
    /// <c>instance_player_unbanned</c> event; null omits it.</param>
    /// <param name="origin">Optional driving surface; null omits it.</param>
    /// <returns>Result of the unban operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or target is null or
    /// whitespace, or when target contains a line break.</exception>
    [Performs(KgsmActions.ServerPlayersBan)]
    KgsmResult Unban(string instanceName, string target, string? actor = null, string? origin = null);

    /// <summary>
    /// Gets the absolute path to an instance's configuration file.
    /// </summary>
    /// <param name="instanceName">The instance to find the config path for.</param>
    /// <returns>Result containing the absolute path to the config file.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName is null or whitespace.</exception>
    [Performs(KgsmActions.ServerConfigRead)]
    KgsmResult FindConfigPath(string instanceName);

    /// <summary>
    /// Reads a single value from an instance's configuration file.
    /// </summary>
    /// <param name="instanceName">The instance whose config to read.</param>
    /// <param name="key">The configuration key to read.</param>
    /// <returns>Result whose standard output holds the value (empty if the key is absent).</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or key is null or whitespace.</exception>
    [Performs(KgsmActions.ServerConfigRead)]
    KgsmResult GetInstanceConfigValue(string instanceName, string key);

    /// <summary>
    /// Reads an instance's whole configuration: every key, its value, and whether
    /// <see cref="SetInstanceConfigValue"/> will accept a change to it.
    /// </summary>
    /// <param name="instanceName">The instance whose config to read.</param>
    /// <param name="settableOnly">
    /// When true, only the keys the setter accepts are returned — the vocabulary a surface offering
    /// to change something should be written in.
    /// </param>
    /// <returns>
    /// The entries, or <see langword="null"/> when the configuration could not be read. An instance
    /// always has a configuration, so an empty list is never the honest answer to a failed read.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when instanceName is null or whitespace.</exception>
    [Performs(KgsmActions.ServerConfigRead)]
    List<InstanceConfigEntry>? GetInstanceConfig(string instanceName, bool settableOnly = false);

    /// <summary>
    /// Sets a single key=value in an instance's configuration file.
    /// </summary>
    /// <remarks>
    /// Only plain runtime values are settable. KGSM refuses identity/structural keys,
    /// the filesystem paths it manages (every *_dir/*_file), and the integration toggles
    /// (enable_firewall_management, enable_command_shortcuts) —
    /// those have dedicated flows. A refused key surfaces as a non-zero
    /// <see cref="KgsmResult"/> rather than an exception.
    /// </remarks>
    /// <param name="instanceName">The instance whose config to modify.</param>
    /// <param name="key">The configuration key to set.</param>
    /// <param name="value">The value to write; may be the empty string, but not null.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName or key is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    [Performs(KgsmActions.ServerConfigWrite)]
    KgsmResult SetInstanceConfigValue(string instanceName, string key, string value, string? actor = null, string? origin = null);

    /// <summary>
    /// Replaces an instance's maintenance windows and records who wrote them.
    /// </summary>
    /// <remarks>
    /// The engine writes <c>maintenance_windows_author</c> with the windows: <paramref name="author"/>, or
    /// nobody when it is null — and windows with no author run nothing. Writing the windows through
    /// <see cref="SetInstanceConfigValue"/> instead leaves them with no author, and the author key itself
    /// is refused there. <paramref name="authorName"/> is the author's display name, written with the
    /// author as <c>maintenance_windows_author_name</c> and removed when the write names nobody.
    /// </remarks>
    /// <param name="instanceName">The instance whose windows to replace.</param>
    /// <param name="packedWindows">The whole list, packed by <see cref="Scheduling.MaintenanceWindowParser"/>; empty for none.</param>
    /// <param name="author">The account id writing them, or null for nobody.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <param name="authorName">The author's display name, or null for none; it needs <paramref name="author"/>.</param>
    /// <returns>Result of the operation; a malformed list or author is a non-zero result.</returns>
    [Performs(KgsmActions.ServerWindowsWrite)]
    KgsmResult SetMaintenanceWindows(string instanceName, string packedWindows, string? author, string? actor = null, string? origin = null, string? authorName = null);

    /// <summary>
    /// Sets the human-readable label a surface renders for an instance.
    /// </summary>
    /// <remarks>
    /// <para>The id is untouched: nothing on disk is renamed, no path changes, and every downstream
    /// store keyed on the id keeps its history. That is what makes this safe to run at any time, on a
    /// running server, as often as somebody likes.</para>
    /// <para>The engine emits <c>instance_display_name_changed</c>
    /// (<see cref="Events.InstanceDisplayNameChangedData"/>) carrying both labels, and an
    /// <c>instance_config_changed</c> naming the key, so a surface can re-label from either.</para>
    /// <para>Escaping is the engine's: quotes, backslashes, backticks and emoji are handed over as
    /// typed and come back byte-identical through both <c>instances info --json</c> and
    /// <c>instances config-list --json</c>. Control characters are the exception — see
    /// <see cref="InstanceDisplayName.Sanitize"/> for what they do to the engine's config-to-JSON
    /// render, and why the label is reduced to one line before it is written.</para>
    /// </remarks>
    /// <param name="instanceId">The instance's id — the identifier, not its current label.</param>
    /// <param name="displayName">The new label, stored as
    /// <see cref="InstanceDisplayName.Sanitize"/> leaves it. The empty string clears it, after which
    /// the instance reads as its id again; only null is rejected.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Install"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Result of the operation.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceId is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when displayName is null.</exception>
    [Performs(KgsmActions.ServerConfigWrite)]
    KgsmResult SetDisplayName(string instanceId, string displayName, string? actor = null, string? origin = null);

    /// <summary>
    /// Writes an instance's operator-authored server note — the free-text sticky note surfaces
    /// render on a game server (mods, rules, a heads-up before joining).
    /// </summary>
    /// <remarks>
    /// <para>The note spans three config keys: the body is base64-encoded under <c>note</c>
    /// (<see cref="InstanceNote"/> explains why), with <c>note_updated_by</c> and
    /// <c>note_updated_at</c> carrying attribution. They are written in that order — attribution
    /// first, body last — so a mid-sequence failure can never credit a new body to the wrong
    /// person; <see cref="InstanceNoteResult.AppliedKeys"/> reports exactly what landed.</para>
    /// <para>An empty <paramref name="body"/> is the <strong>clear</strong>: the body is blanked
    /// while attribution still records who cleared it and when.</para>
    /// <para>Each key write emits its own <c>instance_config_changed</c> event (key only, never the
    /// value), so a surface that renders an audit feed should collapse the two attribution keys.</para>
    /// </remarks>
    /// <param name="instanceName">The instance whose note to write.</param>
    /// <param name="body">The note body; the empty string clears it. Sanitized before storage
    /// (CRLF collapsed, control characters dropped, trimmed).</param>
    /// <param name="actor">Optional audit principal — also stored as <c>note_updated_by</c>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Install"/>.</param>
    /// <returns>Which keys were applied, and the failure detail when one was refused.</returns>
    /// <exception cref="ArgumentException">Thrown when instanceName is null or whitespace, or when
    /// the sanitized body exceeds <see cref="InstanceNote.MaxLength"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when body is null.</exception>
    [Performs(KgsmActions.ServerConfigWrite)]
    InstanceNoteResult SetInstanceNote(string instanceName, string body, string? actor = null, string? origin = null);

    /// <summary>
    /// Subscribes to continuous log streaming for an instance.
    /// This method starts a background process that continuously streams logs from the specified instance
    /// using the KGSM "--follow" flag. The returned LogSubscription object provides events for
    /// receiving log entries, handling errors, and monitoring connection status.
    /// </summary>
    /// <param name="instanceName">The name of the instance to stream logs from.</param>
    /// <param name="cancellationToken">Optional cancellation token to stop the log streaming.</param>
    /// <returns>
    /// A Task that resolves to a LogSubscription object which can be used to:
    /// - Subscribe to log events via the LogReceived event
    /// - Handle errors via the ErrorOccurred event
    /// - Monitor connection status via the StatusChanged event
    /// - Stop the streaming by calling StopAsync() or disposing the subscription
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when instanceName is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the KGSM process fails to start.</exception>
    /// <remarks>
    /// <para>
    /// This method executes the KGSM command: "--instance {instanceName} --logs --follow"
    /// which provides continuous log output until manually stopped.
    /// </para>
    /// <para>
    /// The LogSubscription implements IDisposable and should be properly disposed to clean up resources.
    /// When disposed, it will automatically stop the underlying KGSM process and clean up all resources.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var subscription = await instanceService.SubscribeToLogsAsync("my-server");
    /// subscription.LogReceived += (sender, args) =>
    /// {
    ///     Console.WriteLine($"[{args.LogEntry.Timestamp}] {args.LogEntry.Message}");
    /// };
    ///
    /// // Later, stop the subscription
    /// await subscription.StopAsync();
    /// subscription.Dispose();
    /// </code>
    /// </para>
    /// </remarks>
    [Performs(KgsmActions.ServerConsoleRead)]
    Task<LogSubscription> SubscribeToLogsAsync(string instanceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to continuous log streaming for an instance with filtering options.
    /// This overload allows you to specify log level filtering and custom parsing options.
    /// </summary>
    /// <param name="instanceName">The name of the instance to stream logs from.</param>
    /// <param name="minimumLogLevel">The minimum log level to include in the stream. Logs below this level will be filtered out.</param>
    /// <param name="includeRawLines">Whether to include raw log lines in addition to parsed entries.</param>
    /// <param name="cancellationToken">Optional cancellation token to stop the log streaming.</param>
    /// <returns>A Task that resolves to a LogSubscription object for managing the log stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when instanceName is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the KGSM process fails to start.</exception>
    /// <remarks>
    /// This method provides additional filtering capabilities:
    /// - minimumLogLevel: Only log entries at or above this level will be included
    /// - includeRawLines: When true, the LogEntry.RawLine property will contain the original log line
    /// </remarks>
    [Performs(KgsmActions.ServerConsoleRead)]
    Task<LogSubscription> SubscribeToLogsAsync(string instanceName, LogLevel minimumLogLevel, bool includeRawLines = true, CancellationToken cancellationToken = default);
}
