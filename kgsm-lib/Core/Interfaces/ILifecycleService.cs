using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Service for managing the lifecycle of KGSM instances.
/// Controls the operational state and monitoring of game server instances.
/// </summary>
public interface ILifecycleService
{
    /// <summary>
    /// Launches a game server instance.
    /// </summary>
    /// <param name="instanceName">The name of the instance to start.</param>
    /// <param name="actor">Optional audit principal (who) propagated to KGSM as
    /// <c>$KGSM_EVENT_ACTOR</c> so the emitted event is attributable, written
    /// <c>provider:name</c>. When null/empty the event records with no actor.</param>
    /// <param name="origin">Optional surface (through-what: <c>ui</c>/<c>assistant</c>/
    /// <c>discord</c>/<c>system</c>/<c>api</c>) propagated as <c>$KGSM_EVENT_ORIGIN</c>.
    /// When null/empty, KGSM emits no origin (no fabricated surface).</param>
    /// <param name="force">Start even when KGSM's memory gate would refuse — passes <c>--force</c>.
    /// The gate refuses a start that would leave the node with less free memory than its configured
    /// floor, comparing the instance's own <c>memory_cap_mb</c> (or its blueprint's advisory
    /// <c>min_ram_mb</c>) against what the node reports available. That fallback figure is a vendor
    /// estimate and can overstate what a game really uses, which is what this exists for. It does not
    /// create memory: forcing a start the node genuinely cannot fit invites the OOM killer, which may
    /// take down a different server, or the watchdog supervising them all. Defaults to false, so a
    /// caller that does not ask for it keeps the protection.</param>
    /// <returns>A <see cref="KgsmResult"/> containing the command execution result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    KgsmResult Start(string instanceName, string? actor = null, string? origin = null, bool force = false);

    /// <summary>
    /// Gracefully shuts down a running server instance.
    /// </summary>
    /// <param name="instanceName">The name of the instance to stop.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Start"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Start"/>.</param>
    /// <returns>A <see cref="KgsmResult"/> containing the command execution result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    KgsmResult Stop(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Performs a complete stop and start sequence for an instance.
    /// </summary>
    /// <param name="instanceName">The name of the instance to restart.</param>
    /// <param name="actor">Optional audit principal — see <see cref="Start"/>.</param>
    /// <param name="origin">Optional driving surface — see <see cref="Start"/>.</param>
    /// <returns>A <see cref="KgsmResult"/> containing the command execution result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    KgsmResult Restart(string instanceName, string? actor = null, string? origin = null);

    /// <summary>
    /// Displays comprehensive runtime status for an instance.
    /// </summary>
    /// <param name="instanceName">The name of the instance to get status for.</param>
    /// <returns>A <see cref="KgsmResult"/> containing the status information.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    KgsmResult GetStatus(string instanceName);

    /// <summary>
    /// Checks if an instance is currently running.
    /// </summary>
    /// <param name="instanceName">The name of the instance to check.</param>
    /// <returns><c>true</c> if the instance is active; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    bool IsActive(string instanceName);

    /// <summary>
    /// Retrieves log entries for an instance.
    /// </summary>
    /// <param name="instanceName">The name of the instance to get logs for.</param>
    /// <param name="lines">The number of log lines to retrieve. Defaults to 10.</param>
    /// <returns>A collection of log lines.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the logs cannot be retrieved.</exception>
    ICollection<string> GetLogs(string instanceName, int lines = 10);

    /// <summary>
    /// Retrieves log entries for an instance asynchronously.
    /// </summary>
    /// <param name="instanceName">The name of the instance to get logs for.</param>
    /// <param name="lines">The number of log lines to retrieve. Defaults to 10.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, containing a collection of log lines.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instanceName"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the logs cannot be retrieved.</exception>
    Task<ICollection<string>> GetLogsAsync(string instanceName, int lines = 10, CancellationToken cancellationToken = default);
}
