using System.Text.Json;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Provides a generic execution interface for KGSM commands with built-in validation,
/// logging, and result parsing.
/// </summary>
public interface IKgsmCommandExecutor
{
    /// <summary>
    /// Executes a KGSM command and deserializes the JSON output to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the JSON output to.</typeparam>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <param name="configureOptions">Optional action to customize JSON serializer options.</param>
    /// <param name="defaultValue">The default value to return if execution or deserialization fails.</param>
    /// <returns>The deserialized result, or the default value if the operation fails.</returns>
    T? ExecuteForJson<T>(
        string[] args,
        Action<JsonSerializerOptions>? configureOptions = null,
        T? defaultValue = default);

    /// <summary>
    /// Executes a KGSM command asynchronously and deserializes the JSON output to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the JSON output to.</typeparam>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <param name="configureOptions">Optional action to customize JSON serializer options.</param>
    /// <param name="defaultValue">The default value to return if execution or deserialization fails.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The deserialized result, or the default value if the operation fails.</returns>
    Task<T?> ExecuteForJsonAsync<T>(
        string[] args,
        Action<JsonSerializerOptions>? configureOptions = null,
        T? defaultValue = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a KGSM command and returns the raw result wrapped in a KgsmResult,
    /// using the configured default timeout.
    /// </summary>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    KgsmResult Execute(params string[] args);

    /// <summary>
    /// Executes a KGSM command with an explicit timeout and returns the raw result
    /// wrapped in a KgsmResult. Use this for long-running operations (install,
    /// update, backup, restore) whose duration exceeds the default timeout.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the command to complete.</param>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    KgsmResult Execute(TimeSpan timeout, params string[] args);

    /// <summary>
    /// Executes a KGSM command with additional environment variables layered onto the
    /// child process, using the configured default timeout. Used to pass per-invocation
    /// provenance (<c>KGSM_EVENT_ACTOR</c> / <c>KGSM_EVENT_ORIGIN</c>) so the events KGSM
    /// emits are attributable downstream, without mutating this process's environment.
    /// </summary>
    /// <param name="environment">Extra environment variables to set on the child process.</param>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    KgsmResult Execute(IReadOnlyDictionary<string, string> environment, params string[] args);

    /// <summary>
    /// Executes a KGSM command with both additional environment variables AND an explicit
    /// timeout. The combination of the previous two overloads — for a long-running mutation
    /// (install, update, backup, restore, uninstall) that also needs provenance stamped so its
    /// event is attributable.
    /// </summary>
    /// <param name="environment">Extra environment variables to set on the child process.</param>
    /// <param name="timeout">Maximum time to wait for the command to complete.</param>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    KgsmResult Execute(IReadOnlyDictionary<string, string> environment, TimeSpan timeout, params string[] args);

    /// <summary>
    /// Executes a KGSM command asynchronously and returns the raw result wrapped in a KgsmResult.
    /// </summary>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    Task<KgsmResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a KGSM command whose exit code is a boolean signal rather than a
    /// success/failure indication (for example, a port check where a non-zero exit
    /// simply means "in use"). Unlike <see cref="Execute(string[])"/>, a non-zero exit
    /// code is logged at Debug level as a normal outcome, never as an error.
    /// </summary>
    /// <param name="args">The command arguments to pass to KGSM.</param>
    /// <returns>A KgsmResult containing the command output and exit code.</returns>
    KgsmResult Probe(params string[] args);

    /// <summary>
    /// Runs a KGSM command that reads a small payload on stdin (a transfer manifest), buffering the
    /// output. A non-zero exit is returned, not thrown: the transfer verbs print a verdict on stdout
    /// even when they refuse.
    /// </summary>
    /// <param name="environment">Provenance variables, or null.</param>
    /// <param name="timeout">Maximum time to wait for the process to exit.</param>
    /// <param name="standardInput">The text written to stdin.</param>
    /// <param name="args">The KGSM arguments.</param>
    KgsmResult ExecuteWithInput(IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, string standardInput, params string[] args);

    /// <summary>
    /// Runs a KGSM command whose stdin and stdout are the caller's streams, copied as they flow and
    /// never buffered whole (a transfer part, tens of gigabytes). Cancelling kills the process tree.
    /// </summary>
    /// <param name="environment">Provenance variables, or null.</param>
    /// <param name="input">Copied to the command's stdin; null leaves it empty.</param>
    /// <param name="output">Receives the command's stdout; null discards it.</param>
    /// <param name="args">The KGSM arguments.</param>
    /// <param name="cancellationToken">Cancelling kills the process and throws <see cref="OperationCanceledException"/>.</param>
    Task<StreamResult> ExecuteStreamingAsync(IReadOnlyDictionary<string, string>? environment, Stream? input, Stream? output, string[] args, CancellationToken cancellationToken = default);
}
