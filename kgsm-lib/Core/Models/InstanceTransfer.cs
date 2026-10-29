using System.Text.Json.Serialization;

namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// What the engine prints for <c>instances export --manifest</c> and reads back on stdin for
/// <c>instances import check|begin</c>: one instance, its blueprint, what the target must have, and the
/// parts that travel. The engine's contract, in the engine's snake_case; a consumer passes it between
/// nodes as this library serializes it and never re-shapes it.
/// </summary>
public record class TransferManifest
{
    /// <summary>The manifest's format version.</summary>
    [JsonPropertyName("format")]
    public int Format { get; set; }

    /// <summary>The engine version that wrote the manifest. A target on another version refuses.</summary>
    [JsonPropertyName("engine_version")]
    public string EngineVersion { get; set; } = string.Empty;

    /// <summary>The instance being moved.</summary>
    [JsonPropertyName("instance")]
    public TransferInstance Instance { get; set; } = new();

    /// <summary>The blueprint the instance was installed from.</summary>
    [JsonPropertyName("blueprint")]
    public TransferBlueprint Blueprint { get; set; } = new();

    /// <summary>The requirement ids the target host must have (<c>container-runtime</c>, <c>steamcmd</c>, <c>steam-credentials</c>).</summary>
    [JsonPropertyName("requirements")]
    public List<string> Requirements { get; set; } = [];

    /// <summary>The parts that travel: the tree and one per container image.</summary>
    [JsonPropertyName("parts")]
    public List<TransferPart> Parts { get; set; } = [];
}

/// <summary>The instance section of a <see cref="TransferManifest"/>.</summary>
public record class TransferInstance
{
    /// <summary>The instance id on the source.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The blueprint name.</summary>
    [JsonPropertyName("blueprint")]
    public string Blueprint { get; set; } = string.Empty;

    /// <summary><c>native</c> or <c>container</c>.</summary>
    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    /// <summary>The display name, null when unset.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>The autostart flag as found before the lock disabled it.</summary>
    [JsonPropertyName("autostart")]
    public bool Autostart { get; set; }

    /// <summary>False when the instance never ran: the import does not start it once.</summary>
    [JsonPropertyName("has_run")]
    public bool HasRun { get; set; }

    /// <summary>The source's library name.</summary>
    [JsonPropertyName("library")]
    public string Library { get; set; } = string.Empty;

    /// <summary>The working directory on the source.</summary>
    [JsonPropertyName("working_dir")]
    public string WorkingDir { get; set; } = string.Empty;

    /// <summary>The ports the instance declares; tcp and udp are separate entries.</summary>
    [JsonPropertyName("ports")]
    public List<TransferPort> Ports { get; set; } = [];
}

/// <summary>One declared port range of a transferring instance.</summary>
public record class TransferPort
{
    /// <summary>The first port.</summary>
    [JsonPropertyName("start")]
    public int Start { get; set; }

    /// <summary>The last port; equal to <see cref="Start"/> for a single port.</summary>
    [JsonPropertyName("end")]
    public int End { get; set; }

    /// <summary><c>tcp</c> or <c>udp</c>.</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;
}

/// <summary>The blueprint section of a <see cref="TransferManifest"/>.</summary>
public record class TransferBlueprint
{
    /// <summary>The blueprint name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>True when the tree part carries the blueprint file and its overrides.</summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; }

    /// <summary>The SHA-256 of the blueprint file, to tell a same-named target copy apart.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>One part of a <see cref="TransferManifest"/>: the tree, or a container image.</summary>
public record class TransferPart
{
    /// <summary><c>tree</c>, or <c>image-N</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The size in bytes: the tree's measured bytes, or the room the image takes once loaded.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>The tree's file count; null on an image part.</summary>
    [JsonPropertyName("files"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Files { get; set; }

    /// <summary>The image reference; null on the tree part.</summary>
    [JsonPropertyName("ref"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ref { get; set; }

    /// <summary>The image id the target compares after loading; null on the tree part.</summary>
    [JsonPropertyName("image_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageId { get; set; }
}

/// <summary>The refusal codes an <see cref="ImportRefusal"/> carries, as the engine names them.</summary>
public static class ImportRefusalCodes
{
    /// <summary>The source and target engines differ in version.</summary>
    public const string EngineVersionMismatch = "engine_version_mismatch";

    /// <summary>The target lacks a requirement the manifest lists.</summary>
    public const string RequirementMissing = "requirement_missing";

    /// <summary>The target library or container runtime lacks the room.</summary>
    public const string InsufficientDisk = "insufficient_disk";

    /// <summary>A declared port overlaps one another instance on the target declares.</summary>
    public const string PortConflict = "port_conflict";

    /// <summary>The target library is offline.</summary>
    public const string LibraryOffline = "library_offline";

    /// <summary>The named library does not exist on the target.</summary>
    public const string LibraryNotFound = "library_not_found";

    /// <summary>The library overlaps the registry's directories.</summary>
    public const string LibraryOverlapsRegistry = "library_overlaps_registry";

    /// <summary>The blueprint is absent on the target.</summary>
    public const string BlueprintMissing = "blueprint_missing";

    /// <summary>A different blueprint of the same name exists on the target.</summary>
    public const string BlueprintCollision = "blueprint_collision";

    /// <summary>The blueprint suffix is not valid.</summary>
    public const string InvalidSuffix = "invalid_suffix";

    /// <summary>The manifest is not a manifest this engine reads.</summary>
    public const string InvalidManifest = "invalid_manifest";
}

/// <summary>One reason the target refuses an import.</summary>
public record class ImportRefusal
{
    /// <summary>The engine exit code this refusal stands for.</summary>
    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    /// <summary>The refusal kind, a value of <see cref="ImportRefusalCodes"/>.</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>The engine's text for the refusal.</summary>
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}

/// <summary>The target's answer to <c>import check</c>, and to a refused <c>import begin</c>: every refusal, none hidden behind the first.</summary>
public record class ImportVerdict
{
    /// <summary>True when the target would accept the import.</summary>
    [JsonPropertyName("ok"), JsonRequired]
    public bool Ok { get; set; }

    /// <summary>Every refusal; empty when <see cref="Ok"/>.</summary>
    [JsonPropertyName("refusals")]
    public List<ImportRefusal> Refusals { get; set; } = [];

    /// <summary>The library the import resolved to.</summary>
    [JsonPropertyName("library")]
    public string Library { get; set; } = string.Empty;

    /// <summary>The blueprint name the import resolved to (suffixed when a suffix applied).</summary>
    [JsonPropertyName("blueprint")]
    public string Blueprint { get; set; } = string.Empty;
}

/// <summary>What <c>import begin --json</c> prints on acceptance.</summary>
public record class ImportBegun
{
    /// <summary>The staged import's id (<c>imp-</c> and twelve hex digits).</summary>
    [JsonPropertyName("import_id")]
    public string ImportId { get; set; } = string.Empty;
}

/// <summary>The outcome of <c>ImportBegin</c>: an import id, or the verdict that refused it.</summary>
/// <param name="ImportId">The staged import's id; null when refused.</param>
/// <param name="Verdict">The refusing verdict; null when the import began.</param>
public record ImportBeginResult(string? ImportId, ImportVerdict? Verdict)
{
    /// <summary>True when the import began.</summary>
    public bool Began => ImportId is not null;
}

/// <summary>What <c>import finish --json</c> prints: the instance the target now holds.</summary>
public record class ImportedInstance
{
    /// <summary>The new instance id on the target.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    /// <summary>The blueprint it runs.</summary>
    [JsonPropertyName("blueprint")]
    public string Blueprint { get; set; } = string.Empty;

    /// <summary>The library it landed in.</summary>
    [JsonPropertyName("library")]
    public string Library { get; set; } = string.Empty;
}

/// <summary>One staged import, a row of <c>import list --json</c>.</summary>
public record class StagedImport
{
    /// <summary>The import id.</summary>
    [JsonPropertyName("import_id")]
    public string ImportId { get; set; } = string.Empty;

    /// <summary>The target library holding the staging directory.</summary>
    [JsonPropertyName("library")]
    public string Library { get; set; } = string.Empty;

    /// <summary>The instance id the manifest names.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    /// <summary>The blueprint the manifest names.</summary>
    [JsonPropertyName("blueprint")]
    public string Blueprint { get; set; } = string.Empty;

    /// <summary>When the import began, as the engine wrote it (ISO 8601, UTC).</summary>
    [JsonPropertyName("began")]
    public string Began { get; set; } = string.Empty;
}

/// <summary>One requirement of a blueprint and whether this host has it.</summary>
public record class BlueprintRequirement
{
    /// <summary>The requirement id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whether this host has it.</summary>
    [JsonPropertyName("present")]
    public bool Present { get; set; }
}

/// <summary>What <c>blueprints requirements --json</c> prints, whether or not every requirement is present.</summary>
public record class BlueprintRequirements
{
    /// <summary>The blueprint asked about.</summary>
    [JsonPropertyName("blueprint")]
    public string Blueprint { get; set; } = string.Empty;

    /// <summary>Each requirement the blueprint declares.</summary>
    [JsonPropertyName("requirements")]
    public List<BlueprintRequirement> Requirements { get; set; } = [];

    /// <summary>True when every requirement is present.</summary>
    [JsonIgnore]
    public bool AllPresent => Requirements.TrueForAll(r => r.Present);
}

/// <summary>
/// The engine's exit codes for the transfer verbs, for a caller branching on
/// <see cref="KgsmResult.ExitCode"/> of a failed export, part, commit or unlock.
/// </summary>
public static class TransferExitCodes
{
    /// <summary>An instance or part the engine does not know.</summary>
    public const int NotFound = 25;

    /// <summary>Another token holds the transfer lock, or the lock is not held.</summary>
    public const int InstanceLocked = 59;

    /// <summary>The source and target engine versions differ.</summary>
    public const int EngineVersionMismatch = 60;

    /// <summary>A requirement is absent.</summary>
    public const int RequirementMissing = 61;

    /// <summary>A declared port overlaps another instance's.</summary>
    public const int PortConflict = 62;

    /// <summary>A part could not be extracted, an image id differs, or a count differs.</summary>
    public const int TransferIntegrity = 63;

    /// <summary>The import id is unknown.</summary>
    public const int ImportNotFound = 64;
}

/// <summary>
/// A value read from an engine verb together with the verb's raw result. <see cref="Value"/> is set
/// exactly when <see cref="Result"/> succeeded; otherwise the caller reads the exit code and stderr.
/// </summary>
/// <typeparam name="T">The value the verb prints.</typeparam>
/// <param name="Result">The verb's result.</param>
/// <param name="Value">The parsed value; null on failure.</param>
public record TransferOutcome<T>(KgsmResult Result, T? Value) where T : class
{
    /// <summary>True when the verb succeeded and its output was read.</summary>
    public bool IsSuccess => Value is not null;
}

/// <summary>The outcome of a streaming verb: its exit code and stderr; the bytes went to the caller's stream.</summary>
/// <param name="ExitCode">The process exit code. The streamed bytes are data only when this is 0.</param>
/// <param name="Stderr">The captured, size-capped standard error.</param>
public record StreamResult(int ExitCode, string Stderr)
{
    /// <summary>True when the process exited 0.</summary>
    public bool IsSuccess => ExitCode == 0;
}
