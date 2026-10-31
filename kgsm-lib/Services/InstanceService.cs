using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Exceptions;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Implementation of the IInstanceService interface for managing instances in KGSM.
/// </summary>
public class InstanceService : IInstanceService
{
    private readonly IKgsmCommandExecutor _commandExecutor;
    private readonly ILogSubscriptionService _logSubscriptionService;
    private readonly ILifecycleService _lifecycleService;
    private readonly KgsmTimeoutOptions _timeouts;
    private readonly ILogger<InstanceService> _logger;

    /// <summary>
    /// Initializes a new instance of the InstanceService class.
    /// </summary>
    /// <param name="commandExecutor">The command executor to use for executing KGSM commands.</param>
    /// <param name="logSubscriptionService">The log subscription service for managing log streams.</param>
    /// <param name="lifecycleService">The lifecycle service for managing instance lifecycle operations.</param>
    /// <param name="logger">The logger to use for logging.</param>
    /// <param name="kgsmOptions">
    /// KGSM options, used here for the per-operation timeouts. Optional: when null
    /// (e.g. in tests that don't exercise timeouts), generous defaults are used.
    /// The DI container injects the registered instance.
    /// </param>
    public InstanceService(
        IKgsmCommandExecutor commandExecutor,
        ILogSubscriptionService logSubscriptionService,
        ILifecycleService lifecycleService,
        ILogger<InstanceService> logger,
        KgsmOptions? kgsmOptions = null)
    {
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _logSubscriptionService = logSubscriptionService ?? throw new ArgumentNullException(nameof(logSubscriptionService));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _timeouts = kgsmOptions?.Timeouts ?? new KgsmTimeoutOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("InstanceService initialized");
    }

    /// <inheritdoc/>
    public Dictionary<string, Instance> GetAll()
    {
        return GetAllOrNull() ?? [];
    }

    /// <inheritdoc/>
    public Dictionary<string, Instance>? GetAllOrNull()
    {
        // ExecuteForJson returns null on a non-zero exit OR unparseable output, and the deserialized
        // value on success. KGSM emits "{}" for an empty roster, which deserializes to an empty (NON-null)
        // dictionary — so null here unambiguously means the read FAILED, never "zero instances". GetAll()
        // keeps the lenient "?? []" for callers that don't care; this preserves the distinction.
        return _commandExecutor.ExecuteForJson<Dictionary<string, Instance>>(["instances", "list", "--detailed", "--json"]);
    }

    /// <inheritdoc/>
    public Instance? GetInstanceInfo(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _commandExecutor.ExecuteForJson<Instance>(["instances", "info", instanceName, "--json"]);
    }

    /// <inheritdoc/>
    public InstanceRuntimeStatus? GetInstanceStatus(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _commandExecutor.ExecuteForJson<InstanceRuntimeStatus>(["instances", "status", instanceName, "--json"]);
    }

    /// <inheritdoc/>
    public Dictionary<string, Reading<InstanceRuntimeStatus>> GetAllStatuses(bool fast = false)
    {
        string[] args = fast
            ? ["instances", "list", "--status", "--json", "--fast"]
            : ["instances", "list", "--status", "--json"];

        return _commandExecutor.ExecuteForJson<Dictionary<string, Reading<InstanceRuntimeStatus>>>(args) ?? [];
    }

    /// <inheritdoc/>
    public KgsmResult Install(string blueprintName, string? library = null, string? version = null, string? displayName = null, string? actor = null, string? origin = null, int? port = null, bool? start = null, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(blueprintName, nameof(blueprintName));

        List<string> args = ["install", blueprintName];

        if (library is not null)
        {
            args.Add("--library");
            args.Add(library);
        }

        if (version is not null)
        {
            args.Add("--version");
            args.Add(version);
        }

        if (displayName is not null)
        {
            args.Add("--name");
            args.Add(displayName);
        }

        if (id is not null)
        {
            args.Add("--id");
            args.Add(id);
        }

        if (port is not null)
        {
            args.Add("--port");
            args.Add(port.Value.ToString());
        }

        if (start == true)
        {
            args.Add("--start");
        }

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Install, args.ToArray())
            : _commandExecutor.Execute(provenance, _timeouts.Install, args.ToArray());
    }

    /// <inheritdoc/>
    public KgsmResult Uninstall(string instanceName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        // Always --force: a programmatic uninstall through the library is, by definition, already
        // confirmed at the calling surface (e.g. the API's operator gate + the SPA's confirm flow). kgsm's
        // uninstall is interactive by default and returns a non-zero EC_CANCELLED with no TTY — so without
        // --force this would silently no-op. The destructive confirmation belongs at the product surface,
        // never as a TTY prompt to a non-interactive engine call.
        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Uninstall, "uninstall", instanceName, "--force")
            : _commandExecutor.Execute(provenance, _timeouts.Uninstall, "uninstall", instanceName, "--force");
    }

    /// <inheritdoc/>
    public KgsmResult Move(string instanceName, string library, bool skipSpaceCheck = false, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));
        ArgumentNullException.ThrowIfNull(library, nameof(library));

        List<string> args = ["instances", "move", instanceName, "--library", library];

        if (skipSpaceCheck)
        {
            args.Add("--skip-space-check");
        }

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Move, args.ToArray())
            : _commandExecutor.Execute(provenance, _timeouts.Move, args.ToArray());
    }

    /// <inheritdoc/>
    public TransferOutcome<TransferManifest> ExportManifest(string instanceName, string? lockToken = null, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        List<string> args = ["instances", "export", instanceName, "--manifest"];
        if (lockToken is not null)
        {
            args.Add("--lock");
            args.Add(lockToken);
        }

        KgsmResult result = Run(actor, origin, _timeouts.Default, args);
        return Read(result, KgsmJsonContext.Default.TransferManifest);
    }

    /// <inheritdoc/>
    public Task<StreamResult> ExportPartAsync(string instanceName, string partId, string lockToken, Stream destination, string? actor = null, string? origin = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));
        ArgumentNullException.ThrowIfNull(partId, nameof(partId));
        ArgumentNullException.ThrowIfNull(lockToken, nameof(lockToken));
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        return _commandExecutor.ExecuteStreamingAsync(
            KgsmProvenance.Build(actor, origin), null, destination,
            ["instances", "export", instanceName, "--part", partId, "--lock", lockToken],
            cancellationToken);
    }

    /// <inheritdoc/>
    public KgsmResult CommitExport(string instanceName, string lockToken, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));
        ArgumentNullException.ThrowIfNull(lockToken, nameof(lockToken));

        return Run(actor, origin, _timeouts.Uninstall, ["instances", "export", instanceName, "--commit", "--lock", lockToken]);
    }

    /// <inheritdoc/>
    public KgsmResult Unlock(string instanceName, string? lockToken = null, bool force = false, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));
        if (lockToken is null && !force)
            throw new ArgumentException("Unlock needs the lock token, or force.", nameof(lockToken));

        List<string> args = ["instances", "unlock", instanceName];
        if (force)
        {
            args.Add("--force");
        }
        else
        {
            args.Add("--lock");
            args.Add(lockToken!);
        }

        return Run(actor, origin, _timeouts.Default, args);
    }

    /// <inheritdoc/>
    public ImportVerdict ImportCheck(TransferManifest manifest, string? library = null, string? blueprintSuffix = null, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));

        KgsmResult result = RunWithManifest(manifest, ["instances", "import", "check", "--json"], library, blueprintSuffix, actor, origin);
        return ReadVerdict(result) ?? throw Unreadable("import check", result);
    }

    /// <inheritdoc/>
    public ImportBeginResult ImportBegin(TransferManifest manifest, string? library = null, string? blueprintSuffix = null, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(manifest, nameof(manifest));

        KgsmResult result = RunWithManifest(manifest, ["instances", "import", "begin", "--json"], library, blueprintSuffix, actor, origin);

        // A refusal prints the verdict instead of the id and exits non-zero; the stdout is read first.
        if (result.IsSuccess)
        {
            ImportBegun? begun = Deserialize(result.Stdout, KgsmJsonContext.Default.ImportBegun);
            if (begun is not null && begun.ImportId.Length > 0)
                return new ImportBeginResult(begun.ImportId, null);
        }
        else if (ReadVerdict(result) is { } verdict)
        {
            return new ImportBeginResult(null, verdict);
        }

        throw Unreadable("import begin", result);
    }

    /// <inheritdoc/>
    public Task<StreamResult> ImportPartAsync(string importId, string partId, Stream source, string? actor = null, string? origin = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importId, nameof(importId));
        ArgumentNullException.ThrowIfNull(partId, nameof(partId));
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        return _commandExecutor.ExecuteStreamingAsync(
            KgsmProvenance.Build(actor, origin), source, null,
            ["instances", "import", "part", importId, partId],
            cancellationToken);
    }

    /// <inheritdoc/>
    public TransferOutcome<ImportedInstance> ImportFinish(string importId, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(importId, nameof(importId));

        // Finishing installs and starts the instance once, so it takes an install's time.
        KgsmResult result = Run(actor, origin, _timeouts.Install, ["instances", "import", "finish", importId, "--json"]);
        return Read(result, KgsmJsonContext.Default.ImportedInstance);
    }

    /// <inheritdoc/>
    public KgsmResult ImportAbort(string importId, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(importId, nameof(importId));

        return Run(actor, origin, _timeouts.Default, ["instances", "import", "abort", importId]);
    }

    /// <inheritdoc/>
    public TransferOutcome<List<StagedImport>> ImportList()
    {
        KgsmResult result = _commandExecutor.Execute(["instances", "import", "list", "--json"]);
        return Read(result, KgsmJsonContext.Default.ListStagedImport);
    }

    private KgsmResult Run(string? actor, string? origin, TimeSpan timeout, List<string> args)
    {
        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(timeout, args.ToArray())
            : _commandExecutor.Execute(provenance, timeout, args.ToArray());
    }

    private KgsmResult RunWithManifest(TransferManifest manifest, List<string> args, string? library, string? blueprintSuffix, string? actor, string? origin)
    {
        if (library is not null)
        {
            args.Add("--library");
            args.Add(library);
        }

        if (blueprintSuffix is not null)
        {
            args.Add("--blueprint-suffix");
            args.Add(blueprintSuffix);
        }

        string json = JsonSerializer.Serialize(manifest, KgsmJsonContext.Default.TransferManifest);
        return _commandExecutor.ExecuteWithInput(KgsmProvenance.Build(actor, origin), _timeouts.Default, json, args.ToArray());
    }

    private static TransferOutcome<T> Read<T>(KgsmResult result, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (!result.IsSuccess)
            return new TransferOutcome<T>(result, null);

        T? value = Deserialize(result.Stdout, typeInfo);
        return value is null
            ? throw new KgsmException($"The engine exited 0 but printed nothing readable as {typeof(T).Name}: {Excerpt(result.Stdout)}")
            : new TransferOutcome<T>(result, value);
    }

    // The verdict's "ok" is required on the wire, so an import id or any other object is not read as one.
    private static ImportVerdict? ReadVerdict(KgsmResult result) =>
        Deserialize(result.Stdout, KgsmJsonContext.Default.ImportVerdict);

    private static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static KgsmException Unreadable(string verb, KgsmResult result) =>
        new($"kgsm {verb} exited {result.ExitCode} without a readable answer: {Excerpt(string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr)}");

    private static string Excerpt(string text) => text.Length <= 300 ? text : text[..300] + "...";

    /// <inheritdoc/>
    public ICollection<string> GetLogs(string instanceName, int lines = 10)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.GetLogs(instanceName, lines);
    }

    /// <inheritdoc/>
    public async Task<ICollection<string>> GetLogsAsync(string instanceName, int lines = 10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return await _lifecycleService.GetLogsAsync(instanceName, lines, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public KgsmResult GetStatus(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.GetStatus(instanceName);
    }

    /// <inheritdoc/>
    public KgsmResult GetInfo(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _commandExecutor.Execute("instances", "info", instanceName);
    }

    /// <inheritdoc/>
    public bool IsActive(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.IsActive(instanceName);
    }

    /// <inheritdoc/>
    public KgsmResult Start(string instanceName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.Start(instanceName, actor, origin);
    }

    /// <inheritdoc/>
    public KgsmResult Stop(string instanceName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.Stop(instanceName, actor, origin);
    }

    /// <inheritdoc/>
    public KgsmResult Restart(string instanceName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _lifecycleService.Restart(instanceName, actor, origin);
    }

    /// <inheritdoc/>
    public KgsmResult GetInstalledVersion(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _commandExecutor.Execute("instances", "version", instanceName, "--installed");
    }

    /// <inheritdoc/>
    public KgsmResult GetLatestVersion(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        // Reaches the game's upstream, exactly like check-update — so it gets the same ceiling, not
        // the tight default meant for local reads.
        return _commandExecutor.Execute(_timeouts.UpdateCheck, "instances", "version", instanceName, "--latest");
    }

    /// <inheritdoc/>
    public KgsmResult CheckUpdate(string instanceName, bool emit = false, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        string[] args = emit
            ? ["instances", "check-update", instanceName, "--emit"]
            : ["instances", "check-update", instanceName];

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.UpdateCheck, args)
            : _commandExecutor.Execute(provenance, _timeouts.UpdateCheck, args);
    }

    /// <inheritdoc/>
    public KgsmResult Update(string instanceName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Update, "instances", "update", instanceName)
            : _commandExecutor.Execute(provenance, _timeouts.Update, "instances", "update", instanceName);
    }

    /// <inheritdoc/>
    public KgsmResult GetBackups(string instanceName)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        return _commandExecutor.Execute("instances", "backups", instanceName);
    }

    /// <inheritdoc/>
    public List<InstanceBackup> GetBackupsDetailed(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));

        // ExecuteForJson returns null on a non-zero exit or unparseable output; an instance with no
        // backups prints an empty array. Both collapse to an empty list here — see the interface doc.
        return _commandExecutor.ExecuteForJson<List<InstanceBackup>>(
            ["instances", "backups", instanceName, "--json"]) ?? [];
    }

    /// <inheritdoc/>
    public KgsmResult CreateBackup(string instanceName, string? actor = null, string? origin = null,
        string? reason = null, string? retention = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));

        // Checked before the process is spawned. The engine refuses an unknown value too, but only
        // after resolving the instance — and a typo'd reason should not cost the caller a round trip
        // to find out, nor risk landing as an unrecognised word in the one record of what a backup is.
        if (reason is not null && !BackupReason.All.Contains(reason))
            throw new ArgumentException(
                $"Unknown backup reason '{reason}'. One of: {string.Join(", ", BackupReason.All)}.", nameof(reason));

        if (retention is not null && !BackupRetention.All.Contains(retention))
            throw new ArgumentException(
                $"Unknown backup retention '{retention}'. One of: {string.Join(", ", BackupRetention.All)}.", nameof(retention));

        List<string> args = ["instances", "create-backup", instanceName];
        if (reason is not null) args.Add($"--reason={reason}");
        if (retention is not null) args.Add($"--retention={retention}");

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Backup, [.. args])
            : _commandExecutor.Execute(provenance, _timeouts.Backup, [.. args]);
    }

    /// <inheritdoc/>
    public KgsmResult PinBackup(string instanceName, string backupName, string? actor = null, string? origin = null)
        => SetBackupRetention("pin-backup", instanceName, backupName, actor, origin);

    /// <inheritdoc/>
    public KgsmResult UnpinBackup(string instanceName, string backupName, string? actor = null, string? origin = null)
        => SetBackupRetention("unpin-backup", instanceName, backupName, actor, origin);

    // Both verbs differ only in the word they send, so they share one path — the argument checks and
    // the provenance stamping cannot drift between pinning and unpinning.
    private KgsmResult SetBackupRetention(string verb, string instanceName, string backupName,
        string? actor, string? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(backupName, nameof(backupName));

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Backup, "instances", verb, instanceName, backupName)
            : _commandExecutor.Execute(provenance, _timeouts.Backup, "instances", verb, instanceName, backupName);
    }

    /// <inheritdoc/>
    public KgsmResult PruneBackups(string instanceName, int keepN, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentOutOfRangeException.ThrowIfLessThan(keepN, 1, nameof(keepN));

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Backup, "instances", "prune-backups", instanceName, $"--keep={keepN}")
            : _commandExecutor.Execute(provenance, _timeouts.Backup, "instances", "prune-backups", instanceName, $"--keep={keepN}");
    }

    /// <inheritdoc/>
    public KgsmResult DeleteBackup(string instanceName, string backupName, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(backupName, nameof(backupName));

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Backup, "instances", "delete-backup", instanceName, backupName)
            : _commandExecutor.Execute(provenance, _timeouts.Backup, "instances", "delete-backup", instanceName, backupName);
    }

    /// <inheritdoc/>
    public KgsmResult RestoreBackup(string instanceName, string backupName, string? actor = null, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(instanceName, nameof(instanceName));
        ArgumentNullException.ThrowIfNull(backupName, nameof(backupName));

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute(_timeouts.Restore, "instances", "restore-backup", instanceName, backupName)
            : _commandExecutor.Execute(provenance, _timeouts.Restore, "instances", "restore-backup", instanceName, backupName);
    }

    /// <inheritdoc/>
    public KgsmResult GenerateId(string blueprintName, string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintName, nameof(blueprintName));

        var args = new List<string> { "instances", "generate-id", blueprintName };

        if (!string.IsNullOrWhiteSpace(id))
        {
            args.Add("--id");
            args.Add(id);
        }

        return _commandExecutor.Execute(args.ToArray());
    }

    /// <inheritdoc/>
    public KgsmResult Save(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));

        return _commandExecutor.Execute("instances", "save", instanceName);
    }

    /// <inheritdoc/>
    public KgsmResult SendInput(string instanceName, string command, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(command, nameof(command));

        // input is a quick command, so the default-timeout env overload carries provenance
        // onto the instance_input_sent event kgsm emits (same pattern as SetInstanceConfigValue).
        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute("instances", "input", instanceName, command)
            : _commandExecutor.Execute(provenance, "instances", "input", instanceName, command);
    }

    /// <inheritdoc/>
    public KgsmResult Announce(string instanceName, string message, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        // A console reads one command per line, so a line break in the message would
        // deliver a second command nobody issued. The engine refuses this too; failing
        // here as well means a caller finds out at the call site rather than through a
        // process exit code, and no malformed argument is spawned in the first place.
        if (message.Contains('\n') || message.Contains('\r'))
        {
            throw new ArgumentException(
                "Announcement message must not contain a line break.", nameof(message));
        }

        // announce is a quick command, so the default-timeout env overload carries
        // provenance onto the instance_announcement_sent event kgsm emits (same pattern
        // as SendInput and Moderate).
        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute("instances", "announce", instanceName, message)
            : _commandExecutor.Execute(provenance, "instances", "announce", instanceName, message);
    }

    /// <inheritdoc/>
    public KgsmResult Kick(string instanceName, string target, string? actor = null, string? origin = null)
        => Moderate("kick", instanceName, target, actor, origin);

    /// <inheritdoc/>
    public KgsmResult Ban(string instanceName, string target, string? actor = null, string? origin = null)
        => Moderate("ban", instanceName, target, actor, origin);

    /// <inheritdoc/>
    public KgsmResult Unban(string instanceName, string target, string? actor = null, string? origin = null)
        => Moderate("unban", instanceName, target, actor, origin);

    /// <summary>
    /// Runs one moderation verb. The three differ only in which blueprint-declared
    /// template the engine resolves, so the target is passed through untouched and the
    /// verb selects the template on the far side.
    /// </summary>
    private KgsmResult Moderate(string verb, string instanceName, string target, string? actor, string? origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));

        // A console reads one command per line, so a line break in the target would
        // deliver a second command nobody issued. The engine refuses this too; failing
        // here as well means a caller finds out at the call site rather than through a
        // process exit code, and no malformed argument is spawned in the first place.
        if (target.Contains('\n') || target.Contains('\r'))
        {
            throw new ArgumentException(
                "Moderation target must not contain a line break.", nameof(target));
        }

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute("instances", verb, instanceName, target)
            : _commandExecutor.Execute(provenance, "instances", verb, instanceName, target);
    }

    /// <inheritdoc/>
    public KgsmResult FindConfigPath(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));

        return _commandExecutor.Execute("instances", "find", instanceName);
    }

    /// <inheritdoc/>
    public KgsmResult GetInstanceConfigValue(string instanceName, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        return _commandExecutor.Execute("instances", "config-get", instanceName, key);
    }

    /// <inheritdoc/>
    public List<InstanceConfigEntry>? GetInstanceConfig(string instanceName, bool settableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));

        // Null is carried through rather than collapsed to an empty list: an instance always has a
        // configuration, so an empty result can only mean the read failed, and reporting that as
        // "this server has no settings" is a fabrication a caller cannot see through.
        string[] args = settableOnly
            ? ["instances", "config-list", instanceName, "--settable", "--json"]
            : ["instances", "config-list", instanceName, "--json"];

        return _commandExecutor.ExecuteForJson<List<InstanceConfigEntry>>(args);
    }

    /// <inheritdoc/>
    public KgsmResult SetInstanceConfigValue(string instanceName, string key, string value, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        // The value may legitimately be the empty string (e.g. clearing
        // executable_arguments), so only null is rejected.
        ArgumentNullException.ThrowIfNull(value, nameof(value));

        // The whole assignment rides as a single argv element; kgsm splits it on
        // the first '=' only, so a value containing '=' is preserved. config-set is a
        // quick command, so the default-timeout env overload carries provenance.
        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute("instances", "config-set", instanceName, $"{key}={value}")
            : _commandExecutor.Execute(provenance, "instances", "config-set", instanceName, $"{key}={value}");
    }

    /// <inheritdoc/>
    public KgsmResult SetMaintenanceWindows(string instanceName, string packedWindows, string? author, string? actor = null, string? origin = null, string? authorName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        ArgumentNullException.ThrowIfNull(packedWindows, nameof(packedWindows));

        List<string> args = ["instances", "config-set", instanceName, $"maintenance_windows={packedWindows}"];
        if (!string.IsNullOrWhiteSpace(author))
        {
            args.Add("--author");
            args.Add(author);
            if (!string.IsNullOrWhiteSpace(authorName))
            {
                args.Add("--author-name");
                args.Add(authorName);
            }
        }

        IReadOnlyDictionary<string, string>? provenance = KgsmProvenance.Build(actor, origin);
        return provenance is null
            ? _commandExecutor.Execute([.. args])
            : _commandExecutor.Execute(provenance, [.. args]);
    }

    /// <inheritdoc/>
    public KgsmResult SetDisplayName(string instanceId, string displayName, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId, nameof(instanceId));
        // The empty string is the clear — the instance then reads as its id again; only null is rejected.
        ArgumentNullException.ThrowIfNull(displayName, nameof(displayName));

        // config-set rather than the `rename` verb, though the engine routes both through the same
        // writer and emits the same two events. rename takes the label as trailing positionals and
        // still reads `-h`/`--help`/`help` among them as its own flag, so those three labels would
        // print usage and report success without writing anything. config-set carries the whole
        // assignment as one argv element, which no label text can be mistaken for.
        return SetInstanceConfigValue(
            instanceId, "display_name", InstanceDisplayName.Sanitize(displayName), actor, origin);
    }

    /// <inheritdoc/>
    public InstanceNoteResult SetInstanceNote(string instanceName, string body, string? actor = null, string? origin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName, nameof(instanceName));
        // The empty string is the clear; only null is rejected.
        ArgumentNullException.ThrowIfNull(body, nameof(body));

        // Encode (and length-check) BEFORE any write, so an over-long body throws with the config
        // untouched rather than after the attribution keys have already landed.
        string encoded = InstanceNote.Encode(body);
        string updatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Attribution first, body LAST — see InstanceNoteResult for why the order is load-bearing.
        var applied = new List<string>(3);
        foreach ((string key, string value) in new[]
        {
            (InstanceNote.UpdatedByKey, actor ?? string.Empty),
            (InstanceNote.UpdatedAtKey, updatedAt),
            (InstanceNote.BodyKey, encoded),
        })
        {
            KgsmResult result = SetInstanceConfigValue(instanceName, key, value, actor, origin);
            if (!result.IsSuccess)
                return new InstanceNoteResult(false, applied, key,
                    string.IsNullOrWhiteSpace(result.Stderr) ? null : result.Stderr.Trim(), result.ExitCode);

            applied.Add(key);
        }

        return new InstanceNoteResult(true, applied);
    }

    /// <inheritdoc/>
    public Task<LogSubscription> SubscribeToLogsAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        return _logSubscriptionService
            .SubscribeToLogsAsync(instanceName, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<LogSubscription> SubscribeToLogsAsync(string instanceName, Core.Models.Enums.LogLevel minimumLogLevel, bool includeRawLines = true, CancellationToken cancellationToken = default)
    {
        return _logSubscriptionService
            .SubscribeToLogsAsync(instanceName, minimumLogLevel, includeRawLines, cancellationToken);
    }
}
