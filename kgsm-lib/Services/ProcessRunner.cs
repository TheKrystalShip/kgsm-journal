using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Implementation of the IProcessRunner interface for executing processes.
/// </summary>
public class ProcessRunner : IProcessRunner
{
    private const int EXIT_CODE_SUCCESS = 0;
    private const int EXIT_CODE_FAILURE = 1;

    /// <summary>
    /// Fallback timeout for the parameterless-timeout overload. This is a generic
    /// safety net for direct callers; KGSM operations route their per-operation
    /// timeout (see <see cref="TheKrystalShip.KGSM.Core.Models.KgsmTimeoutOptions"/>)
    /// in explicitly via the command executor, so this value rarely applies in practice.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ProcessRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the ProcessRunner class.
    /// </summary>
    /// <param name="logger">The logger to use for logging.</param>
    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("ProcessRunner initialized");
    }

    /// <inheritdoc/>
    public ProcessResult Execute(string command, params string[] args)
        => Execute(command, DefaultTimeout, null, args);

    /// <inheritdoc/>
    public ProcessResult Execute(string command, TimeSpan timeout, params string[] args)
        => Execute(command, timeout, null, args);

    /// <inheritdoc/>
    public ProcessResult Execute(string command, TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string[] args)
        => ExecuteBuffered(command, timeout, environment, null, args);

    /// <inheritdoc/>
    public ProcessResult ExecuteWithInput(string command, TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string standardInput, string[] args)
    {
        ArgumentNullException.ThrowIfNull(standardInput, nameof(standardInput));
        return ExecuteBuffered(command, timeout, environment, standardInput, args);
    }

    private ProcessResult ExecuteBuffered(string command, TimeSpan timeout, IReadOnlyDictionary<string, string>? environment, string? standardInput, string[] args)
    {
        ArgumentNullException.ThrowIfNull(command, nameof(command));

        string arguments = args.Length > 0 ? string.Join(" ", args) : string.Empty;

        _logger.LogDebug("Executing command: {Command} {Arguments}", command, arguments);

        ProcessStartInfo processStartInfo = new()
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Pass each arg as a DISTINCT argv element (ArgumentList), never a single
        // space-joined Arguments string — that string is re-split on whitespace and
        // would mangle ANY argument containing a space (a console command, a config
        // value, an instance name with spaces). `arguments` above is for logging only.
        foreach (string arg in args)
            processStartInfo.ArgumentList.Add(arg);

        // Layer caller-supplied provenance onto the inherited environment (UseShellExecute
        // is false, so ProcessStartInfo.Environment is pre-seeded from this process). We
        // only ADD keys — never replace the environment — and never touch our own.
        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> variable in environment)
            {
                processStartInfo.Environment[variable.Key] = variable.Value;
            }
        }

        Process? process;

        try
        {
            process = Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process: {Command} {Arguments}", command, arguments);
            return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, ex.Message);
        }

        if (process is null)
        {
            _logger.LogError("Process failed to start: {Command} {Arguments}", command, arguments);
            return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, "Process failed to start");
        }

        using (process)
        {
            try
            {
                // Use async reading to prevent deadlocks when buffers fill up
                // This is critical for processes with large output
                List<string> stdoutLines = new();
                List<string> stderrLines = new();

                // Start asynchronous reading of stdout and stderr
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stdoutLines.Add(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stderrLines.Add(e.Data);
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (standardInput is not null)
                {
                    // Output is already draining, so a payload larger than the pipe cannot deadlock.
                    // A process that exits without reading it closes the pipe; its exit code is the answer.
                    try
                    {
                        process.StandardInput.Write(standardInput);
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                    }
                }

                // Wait for process to exit with timeout
                int timeoutMs = ToMilliseconds(timeout);
                bool exited = process.WaitForExit(timeoutMs);

                if (!exited)
                {
                    _logger.LogError("Process timed out after {Timeout}: {Command} {Arguments}", timeout, command, arguments);

                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogWarning(killEx, "Failed to kill timed out process: {Command} {Arguments}", command, arguments);
                    }

                    return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, $"Process execution timed out after {DescribeTimeout(timeout)}");
                }

                // Ensure all output has been read
                process.WaitForExit();

                int exitCode = process.ExitCode;
                string stdout = string.Join(Environment.NewLine, stdoutLines).Trim();
                string stderr = string.Join(Environment.NewLine, stderrLines).Trim();

                if (exitCode != 0)
                {
                    _logger.LogWarning("Command failed with exit code {ExitCode}: {Command} {Arguments}", exitCode, command, arguments);
                    _logger.LogDebug("Command stderr: {Stderr}", stderr);
                }
                else
                {
                    _logger.LogDebug("Command succeeded: {Command} {Arguments}", command, arguments);
                }

                return new ProcessResult(exitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute process: {Command} {Arguments}", command, arguments);
                return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, ex.Message);
            }
        }
    }

    /// <summary>
    /// Converts a timeout to a millisecond value suitable for <see cref="Process.WaitForExit(int)"/>.
    /// A non-positive or infinite timeout maps to -1 (wait indefinitely); very large
    /// finite timeouts are clamped to <see cref="int.MaxValue"/>.
    /// </summary>
    private static int ToMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return Timeout.Infinite;

        double ms = timeout.TotalMilliseconds;
        return ms >= int.MaxValue ? int.MaxValue : (int)ms;
    }

    /// <summary>Human-readable rendering of a timeout for log/error messages.</summary>
    private static string DescribeTimeout(TimeSpan timeout) =>
        timeout.TotalSeconds < 90
            ? $"{timeout.TotalSeconds:0}s"
            : $"{timeout.TotalMinutes:0.#} minutes";

    /// <inheritdoc/>
    public async Task<ProcessResult> ExecuteAsync(string command, string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command, nameof(command));

        string arguments = args.Length > 0 ? string.Join(" ", args) : string.Empty;

        _logger.LogDebug("Executing command asynchronously: {Command} {Arguments}", command, arguments);

        ProcessStartInfo processStartInfo = new()
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Each arg as a distinct argv element — never a space-joined Arguments string
        // (which is re-split on whitespace, mangling any arg with a space). See the sync
        // overload; `arguments` is for logging only.
        foreach (string arg in args)
            processStartInfo.ArgumentList.Add(arg);

        Process? process;

        try
        {
            process = Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process: {Command} {Arguments}", command, arguments);
            return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, ex.Message);
        }

        if (process is null)
        {
            _logger.LogError("Process failed to start: {Command} {Arguments}", command, arguments);
            return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, "Process failed to start");
        }

        using (process)
        {
            try
            {
                // Use async reading to prevent deadlocks when buffers fill up
                // This is critical for processes with large output
                List<string> stdoutLines = new();
                List<string> stderrLines = new();
                object stdoutLock = new();
                object stderrLock = new();

                // Start asynchronous reading of stdout and stderr
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (stdoutLock)
                        {
                            stdoutLines.Add(e.Data);
                        }
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (stderrLock)
                        {
                            stderrLines.Add(e.Data);
                        }
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Register cancellation
                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            _logger.LogWarning("Process cancelled: {Command} {Arguments}", command, arguments);
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to kill cancelled process: {Command} {Arguments}", command, arguments);
                    }
                }))
                {
                    // Wait for process to exit asynchronously
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                    // Ensure all output has been read
                    process.WaitForExit();

                    int exitCode = process.ExitCode;
                    string stdout;
                    string stderr;

                    lock (stdoutLock)
                    {
                        stdout = string.Join(Environment.NewLine, stdoutLines).Trim();
                    }

                    lock (stderrLock)
                    {
                        stderr = string.Join(Environment.NewLine, stderrLines).Trim();
                    }

                    if (exitCode != 0)
                    {
                        _logger.LogWarning("Command failed with exit code {ExitCode}: {Command} {Arguments}", exitCode, command, arguments);
                        _logger.LogDebug("Command stderr: {Stderr}", stderr);
                    }
                    else
                    {
                        _logger.LogDebug("Command succeeded: {Command} {Arguments}", command, arguments);
                    }

                    return new ProcessResult(exitCode, stdout, stderr);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Process execution was cancelled: {Command} {Arguments}", command, arguments);
                return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, "Process execution was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute process: {Command} {Arguments}", command, arguments);
                return new ProcessResult(EXIT_CODE_FAILURE, string.Empty, ex.Message);
            }
        }
    }

    /// <summary>Largest stderr kept from a streaming run; the rest is read and dropped.</summary>
    private const int MaxStreamingStderrChars = 64 * 1024;

    /// <summary>Size of the buffer a stream is copied through.</summary>
    private const int StreamCopyBufferBytes = 128 * 1024;

    /// <inheritdoc/>
    public async Task<StreamResult> ExecuteStreamingAsync(
        string command,
        string[] args,
        IReadOnlyDictionary<string, string>? environment,
        Stream? input,
        Stream? output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command, nameof(command));

        string arguments = string.Join(" ", args);
        _logger.LogDebug("Executing streaming command: {Command} {Arguments}", command, arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> variable in environment)
                startInfo.Environment[variable.Key] = variable.Value;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process failed to start");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process: {Command} {Arguments}", command, arguments);
            return new StreamResult(EXIT_CODE_FAILURE, ex.Message);
        }

        using (process)
        {
            // Linked so a failed copy stops the process too, not only the caller's token.
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using CancellationTokenRegistration kill = stop.Token.Register(() => Kill(process, command, arguments));

            Task<string> stderrTask = ReadCappedAsync(process.StandardError);
            Task inputTask = PumpInputAsync(process, input, stop.Token);
            Task outputTask = PumpOutputAsync(process, output, stop.Token);

            try
            {
                await Task.WhenAll(inputTask, outputTask).ConfigureAwait(false);
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false);
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                _logger.LogWarning("Streaming command failed with exit code {ExitCode}: {Command} {Arguments}", process.ExitCode, command, arguments);

            return new StreamResult(process.ExitCode, stderr);
        }
    }

    private void Kill(Process process, string command, string arguments)
    {
        try
        {
            if (!process.HasExited)
            {
                _logger.LogWarning("Process cancelled: {Command} {Arguments}", command, arguments);
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process: {Command} {Arguments}", command, arguments);
        }
    }

    /// <summary>
    /// Copies the caller's stream into the process and closes its stdin. A process that exits without
    /// reading everything closes the pipe; that is its exit code's business, not a copy failure.
    /// </summary>
    private static async Task PumpInputAsync(Process process, Stream? input, CancellationToken ct)
    {
        try
        {
            if (input is not null)
                await input.CopyToAsync(process.StandardInput.BaseStream, StreamCopyBufferBytes, ct).ConfigureAwait(false);

            await process.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task PumpOutputAsync(Process process, Stream? output, CancellationToken ct)
    {
        Stream destination = output ?? Stream.Null;
        await process.StandardOutput.BaseStream.CopyToAsync(destination, StreamCopyBufferBytes, ct).ConfigureAwait(false);
        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads a stream to its end, keeping only the first <see cref="MaxStreamingStderrChars"/> characters.</summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        StringBuilder kept = new();
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            int room = MaxStreamingStderrChars - kept.Length;
            if (room > 0)
                kept.Append(buffer, 0, Math.Min(read, room));
        }

        return kept.ToString().Trim();
    }
}
