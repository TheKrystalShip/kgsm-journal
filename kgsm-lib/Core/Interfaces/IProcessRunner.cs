using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Interface for executing processes.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executes a command with the specified arguments, using the default timeout.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <returns>Result of the command execution.</returns>
    ProcessResult Execute(string command, params string[] args);

    /// <summary>
    /// Executes a command with an explicit timeout. If the process does not exit
    /// within <paramref name="timeout"/>, its entire process tree is killed and a
    /// failure result is returned whose error text indicates a timeout.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="timeout">Maximum time to wait for the process to exit.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <returns>Result of the command execution.</returns>
    ProcessResult Execute(string command, TimeSpan timeout, params string[] args);

    /// <summary>
    /// Executes a command with an explicit timeout and additional environment variables
    /// layered onto the inherited process environment. Used to pass per-invocation
    /// provenance (e.g. <c>KGSM_EVENT_ACTOR</c> / <c>KGSM_EVENT_ORIGIN</c>) down to KGSM
    /// so the events it emits are attributable, without mutating this process's own
    /// environment. A <see langword="null"/> or empty <paramref name="environment"/>
    /// behaves exactly like the timeout overload.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="timeout">Maximum time to wait for the process to exit.</param>
    /// <param name="environment">Extra environment variables to set on the child process.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <returns>Result of the command execution.</returns>
    ProcessResult Execute(string command, TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string[] args);

    /// <summary>
    /// Asynchronously executes a command with the specified arguments.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation with the result of the command execution.</returns>
    Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command after writing <paramref name="standardInput"/> to its stdin and closing it,
    /// buffering stdout and stderr as <see cref="Execute(string, TimeSpan, IReadOnlyDictionary{string, string}?, string[])"/>
    /// does. For small payloads such as a transfer manifest; never for a part.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="timeout">Maximum time to wait for the process to exit.</param>
    /// <param name="environment">Extra environment variables for the child, or null.</param>
    /// <param name="standardInput">The text written to stdin as UTF-8.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <returns>Result of the command execution.</returns>
    ProcessResult ExecuteWithInput(string command, TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string standardInput, string[] args);

    /// <summary>
    /// Executes a command whose stdin and stdout are the caller's streams, copied as they flow and
    /// never buffered whole. stderr is captured, size-capped. There is no timeout: the caller's token
    /// is the only way to stop it, and cancelling kills the process tree.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="args">Arguments to pass to the command.</param>
    /// <param name="environment">Extra environment variables for the child, or null.</param>
    /// <param name="input">Read to its end into the process's stdin, which is then closed; null leaves stdin closed and empty.</param>
    /// <param name="output">Receives the process's stdout; null discards it.</param>
    /// <param name="cancellationToken">Cancelling kills the process tree and throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The exit code and the captured stderr. Bytes written to <paramref name="output"/> are data only when the exit code is 0.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    Task<StreamResult> ExecuteStreamingAsync(string command, string[] args, IReadOnlyDictionary<string, string>? environment, Stream? input, Stream? output, CancellationToken cancellationToken = default);
}
