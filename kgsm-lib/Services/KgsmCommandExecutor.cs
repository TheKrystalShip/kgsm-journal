using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Extensions;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Generic command executor for KGSM that handles process execution, validation,
/// logging, and result parsing in a centralized way.
/// </summary>
public class KgsmCommandExecutor : IKgsmCommandExecutor
{
    private readonly IProcessRunner _processRunner;
    private readonly string _kgsmPath;
    private readonly TimeSpan _defaultTimeout;
    private readonly ILogger<KgsmCommandExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the KgsmCommandExecutor class.
    /// </summary>
    /// <param name="processRunner">The process runner to use for executing KGSM commands.</param>
    /// <param name="kgsmOptions">The options for KGSM, including the path to the executable.</param>
    /// <param name="logger">The logger to use for logging.</param>
    public KgsmCommandExecutor(
        IProcessRunner processRunner,
        KgsmOptions kgsmOptions,
        ILogger<KgsmCommandExecutor> logger)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _kgsmPath = kgsmOptions.KgsmPath ?? throw new ArgumentNullException(nameof(kgsmOptions.KgsmPath));
        _defaultTimeout = kgsmOptions.Timeouts.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("KgsmCommandExecutor initialized with KGSM path: {KgsmPath}", _kgsmPath);
    }

    /// <inheritdoc/>
    public T? ExecuteForJson<T>(
        string[] args,
        Action<JsonSerializerOptions>? configureOptions = null,
        T? defaultValue = default)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command: {Command}", commandName);

        ProcessResult result = _processRunner.Execute(_kgsmPath, _defaultTimeout, args);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
            return defaultValue;
        }

        // Resolve the contract from the source-generated context (AOT-safe). The rare
        // per-call options tweak is honored by cloning first; callers normally pass null.
        JsonSerializerOptions options = KgsmJson.ExecutorOptions;
        if (configureOptions is not null)
        {
            options = new JsonSerializerOptions(options);
            configureOptions(options);
        }

        try
        {
            var typeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
            var deserialized = JsonSerializer.Deserialize(result.Stdout, typeInfo);

            if (deserialized == null)
            {
                _logger.LogWarning("Deserialization returned null for command: {Command}", commandName);
                return defaultValue;
            }

            _logger.LogDebug("Successfully deserialized result for command: {Command}", commandName);
            return deserialized;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize JSON result for command: {Command}", commandName);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing result for command: {Command}", commandName);
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public async Task<T?> ExecuteForJsonAsync<T>(
        string[] args,
        Action<JsonSerializerOptions>? configureOptions = null,
        T? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command asynchronously: {Command}", commandName);

        ProcessResult result = await _processRunner.ExecuteAsync(_kgsmPath, args, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
            return defaultValue;
        }

        // Resolve the contract from the source-generated context (AOT-safe). The rare
        // per-call options tweak is honored by cloning first; callers normally pass null.
        JsonSerializerOptions options = KgsmJson.ExecutorOptions;
        if (configureOptions is not null)
        {
            options = new JsonSerializerOptions(options);
            configureOptions(options);
        }

        try
        {
            var typeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
            var deserialized = JsonSerializer.Deserialize(result.Stdout, typeInfo);

            if (deserialized == null)
            {
                _logger.LogWarning("Deserialization returned null for command: {Command}", commandName);
                return defaultValue;
            }

            _logger.LogDebug("Successfully deserialized result for command: {Command}", commandName);
            return deserialized;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize JSON result for command: {Command}", commandName);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing result for command: {Command}", commandName);
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public KgsmResult Execute(params string[] args)
        => Execute(_defaultTimeout, args);

    /// <inheritdoc/>
    public KgsmResult Execute(TimeSpan timeout, params string[] args)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command: {Command} (timeout {Timeout})", commandName, timeout);

        ProcessResult result = _processRunner.Execute(_kgsmPath, timeout, args);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
        }
        else
        {
            _logger.LogDebug("Command succeeded: {Command}", commandName);
        }

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public async Task<KgsmResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command asynchronously: {Command}", commandName);

        ProcessResult result = await _processRunner.ExecuteAsync(_kgsmPath, args, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
        }
        else
        {
            _logger.LogDebug("Command succeeded: {Command}", commandName);
        }

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public KgsmResult Execute(IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command with provenance: {Command}", commandName);

        ProcessResult result = _processRunner.Execute(_kgsmPath, _defaultTimeout, environment, args);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
        }
        else
        {
            _logger.LogDebug("Command succeeded: {Command}", commandName);
        }

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public KgsmResult Execute(IReadOnlyDictionary<string, string> environment, TimeSpan timeout, params string[] args)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command with provenance (timeout {Timeout}): {Command}", timeout, commandName);

        ProcessResult result = _processRunner.Execute(_kgsmPath, timeout, environment, args);

        if (result.ExitCode != 0)
        {
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);
        }
        else
        {
            _logger.LogDebug("Command succeeded: {Command}", commandName);
        }

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public KgsmResult Probe(params string[] args)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Probing KGSM command: {Command}", commandName);

        ProcessResult result = _processRunner.Execute(_kgsmPath, _defaultTimeout, args);

        // A non-zero exit code is a normal outcome for a probe (e.g. "port in use"),
        // so it is reported at Debug level rather than logged as an error.
        _logger.LogDebug("Probe completed: {Command} (exit code {ExitCode})", commandName, result.ExitCode);

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public KgsmResult ExecuteWithInput(IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, string standardInput, params string[] args)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command with stdin (timeout {Timeout}): {Command}", timeout, commandName);

        ProcessResult result = _processRunner.ExecuteWithInput(_kgsmPath, timeout, environment, standardInput, args);

        // A refusal is an answer the caller reads from the output, so only a failure that printed
        // nothing is an error here.
        if (result.ExitCode != 0)
            _logger.LogDebug("Command exited {ExitCode}: {Command} - {Error}", result.ExitCode, commandName, result.Stderr);

        return new KgsmResult(result);
    }

    /// <inheritdoc/>
    public async Task<StreamResult> ExecuteStreamingAsync(IReadOnlyDictionary<string, string>? environment, Stream? input, Stream? output, string[] args, CancellationToken cancellationToken = default)
    {
        var commandName = string.Join(" ", args);
        _logger.LogDebug("Executing KGSM command streaming: {Command}", commandName);

        StreamResult result = await _processRunner
            .ExecuteStreamingAsync(_kgsmPath, args, environment, input, output, cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
            _logger.LogError("Command failed: {Command} - {Error}", commandName, result.Stderr);

        return result;
    }
}
