using System.Text.Json.Serialization;
using TheKrystalShip.KGSM.Core.Models.Enums;

namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// Represents an instance of a game server.
/// </summary>
public record class Instance
{
    /// <summary>
    /// Gets or sets the instance's id — the identifier every path, file name, cgroup, event and
    /// downstream store keys on. Auto-generated at install from the blueprint name, unique per
    /// host, and immutable for the instance's lifetime.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable label a surface renders for this instance. Free text —
    /// spaces, punctuation and emoji are all legal, because it never reaches a path — not unique,
    /// and changed at any time with <see cref="Interfaces.IInstanceService.SetDisplayName"/>.
    /// </summary>
    /// <remarks>
    /// <b>Never null and never empty:</b> an instance that has no label of its own reads as its
    /// <see cref="Name"/>. The engine already substitutes the id when the config carries no
    /// <c>display_name</c>, and this derives the same answer for the one case where the engine
    /// states nothing at all — an instance whose library is offline, whose config cannot be read.
    /// Giving that instance a blank label instead would render as a nameless row.
    /// </remarks>
    [JsonPropertyName("display_name")]
    public string DisplayName
    {
        get => string.IsNullOrEmpty(_displayName) ? Name : _displayName;
        set => _displayName = value;
    }

    private string? _displayName;

    /// <summary>
    /// Gets or sets the blueprint file path for the instance.
    /// </summary>
    [JsonPropertyName("blueprint_file")]
    public string BlueprintFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installation date of the instance.
    /// </summary>
    [JsonPropertyName("install_datetime")]
    public DateTime InstallDateTime { get; set; } = DateTime.MinValue;

    /// <summary>
    /// What tells this install apart from any other under the same name. Never changes for the
    /// instance's life; a server removed and installed again under the same name has a different one.
    /// A grant of access names an instance by its node, its name and this nonce. Empty only for an
    /// instance whose library is offline, whose config could not be read.
    /// </summary>
    [JsonPropertyName("install_nonce")]
    public string InstallNonce { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the working directory for the instance.
    /// </summary>
    [JsonPropertyName("working_dir")]
    public string WorkingDir { get; set; } = string.Empty;

        /// <summary>
    /// Gets or sets the backups directory for the instance.
    /// </summary>
    [JsonPropertyName("backups_dir")]
    public string BackupsDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installation directory for the instance — the game binaries
    /// subdirectory of <see cref="WorkingDir"/>, not the root the instance was placed
    /// under. That root is <see cref="LibraryDir"/>.
    /// </summary>
    [JsonPropertyName("install_dir")]
    public string InstallDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the library root the instance lives in.
    /// Empty for an instance the engine has not stamped yet.
    /// </summary>
    [JsonPropertyName("library_dir")]
    public string LibraryDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the library the instance resolves to, or
    /// <c>unregistered</c> when its root matches no registered library. The engine
    /// resolves this from <see cref="LibraryDir"/> per invocation; the instance stores
    /// the path, so a library rename costs nothing.
    /// </summary>
    [JsonPropertyName("library")]
    public string Library { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets where the instance stands relative to that library — reachable, away, or under
    /// a root this host has no entry for. Present on every read the engine makes, mounted or not;
    /// <see langword="null"/> only from an engine that predates libraries.
    /// </summary>
    /// <remarks>
    /// This is the field that says why the rest of this object is mostly empty.
    /// <see cref="InstanceLibraryState.Offline"/> means the instance's config sits behind a
    /// dangling symlink: <see cref="Name"/>, <see cref="Blueprint"/>, <see cref="WorkingDir"/>,
    /// <see cref="LibraryDir"/> and <see cref="Library"/> come from the registry and are real;
    /// every other property is its default because nothing read one, not because it is unset.
    /// </remarks>
    [JsonPropertyName("library_state")]
    public InstanceLibraryState? LibraryState { get; set; }

    /// <summary>
    /// Gets or sets the saves directory for the instance.
    /// </summary>
    [JsonPropertyName("saves_dir")]
    public string SavesDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the temporary directory for the instance.
    /// </summary>
    [JsonPropertyName("temp_dir")]
    public string TempDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the logs directory for the instance.
    /// </summary>
    [JsonPropertyName("logs_dir")]
    public string LogsDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the launch directory for the instance.
    /// </summary>
    [JsonPropertyName("launch_dir")]
    public string LaunchDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable subdirectory for the instance.
    /// </summary>
    [JsonPropertyName("executable_subdirectory")]
    public string ExecutableSubdirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable file for the instance.
    /// </summary>
    [JsonPropertyName("executable_file")]
    public string ExecutableFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the management file for the instance.
    /// </summary>
    [JsonPropertyName("management_file")]
    public string ManagementFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the compose file for the instance (for container instances).
    /// </summary>
    [JsonPropertyName("compose_file")]
    public string ComposeFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version file for the instance.
    /// </summary>
    [JsonPropertyName("version_file")]
    public string VersionFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PID file for the instance.
    /// </summary>
    [JsonPropertyName("pid_file")]
    public string PidFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the socket file for the instance.
    /// </summary>
    [JsonPropertyName("socket_file")]
    public string SocketFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the runtime environment for the instance. This is the sole
    /// supervision discriminator: native instances are supervised by kgsm-watchdog,
    /// container instances by Docker. Binds case-insensitively from KGSM's lowercase
    /// <c>runtime</c> field (no <c>[JsonPropertyName]</c> needed).
    /// <see langword="null"/> when the engine did not report one — it is written in the instance's
    /// own config, which an unmounted library takes with it (<see cref="LibraryState"/> says so).
    /// </summary>
    /// <remarks>
    /// Null is not <see cref="InstanceRuntime.Native"/>. Reading an unreported runtime as native
    /// sends a consumer to the watchdog for a container's run state, and the answer it gets back is
    /// a confident wrong one.
    /// </remarks>
    public InstanceRuntime? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the per-instance cgroup v2 directory KGSM derives for a native
    /// instance (e.g. <c>/sys/fs/cgroup/kgsm.slice/&lt;name&gt;</c>) — the exact path
    /// kgsm-watchdog (re)creates on every native start. <see cref="string.Empty"/> for
    /// container instances (Docker owns their cgroup) and when KGSM omits it. This is the
    /// engine-owned cgroup-path contract: a consumer reading per-instance cgroup counters
    /// (kgsm-monitor) uses this rather than re-deriving the layout or opening the watchdog
    /// socket, and falls back to its own probe (the <c>/proc</c> tree) when the directory
    /// is absent. Bound case-insensitively from KGSM's <c>cgroup_path</c> field.
    /// </summary>
    [JsonPropertyName("cgroup_path")]
    public string CgroupPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platform for the instance.
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether auto-update is enabled for the instance.
    /// </summary>
    [JsonPropertyName("auto_update")]
    public bool AutoUpdate { get; set; } = false;

    /// <summary>
    /// Gets or sets the CPU scheduling priority for the instance (<c>low</c>/<c>normal</c>/<c>high</c>).
    /// Null when KGSM omits <c>cpu_priority</c> — honest unknown, never fabricated as a default.
    /// The watchdog translates it to a cgroup <c>cpu.weight</c> value when enforcing.
    /// </summary>
    [JsonPropertyName("cpu_priority")]
    public string? CpuPriority { get; set; }

    /// <summary>
    /// Gets or sets the memory cap in megabytes for the instance's cgroup.
    /// Null when KGSM omits <c>memory_cap_mb</c> — honest unknown, never fabricated as a default.
    /// </summary>
    [JsonPropertyName("memory_cap_mb")]
    public int? MemoryCapMb { get; set; }

    /// <summary>
    /// Gets or sets the instance's maintenance windows, packed into one value:
    /// <c>daily@05:00/backup;weekly.sun@04:00/backup,update,restart</c>. A window is a schedule, a
    /// <c>/</c>, and the tasks it runs; windows are separated by <c>;</c>. Null or empty means no
    /// maintenance — an absent window is off.
    /// </summary>
    /// <remarks>
    /// Read it with <see cref="Scheduling.MaintenanceWindowParser"/>, which is the grammar's one
    /// implementation and its one validator. Each window carries its own validity, so a window that
    /// cannot be read disables itself and leaves the instance's other windows firing. Written
    /// wholesale: replacing the value is the only way to express deleting a window.
    /// </remarks>
    [JsonPropertyName("maintenance_windows")]
    public string? MaintenanceWindows { get; set; }

    /// <summary>
    /// Gets or sets the account that wrote <see cref="MaintenanceWindows"/>, or null when nobody is
    /// recorded.
    /// </summary>
    /// <remarks>
    /// A window runs only while both the scheduler's service account and this author may do what it
    /// does, so windows with no author run nothing. The engine writes it with the windows and nowhere
    /// else — <see cref="Interfaces.IInstanceService.SetMaintenanceWindows"/> — and clears it on any
    /// windows write that names nobody.
    /// </remarks>
    [JsonPropertyName("maintenance_windows_author")]
    public string? MaintenanceWindowsAuthor { get; set; }

    /// <summary>
    /// Gets or sets the display name of <see cref="MaintenanceWindowsAuthor"/> as it was when the
    /// windows were written, or null when none was recorded.
    /// </summary>
    /// <remarks>
    /// Free text for a surface to show beside the account id. The engine writes it with the author and
    /// removes it with any windows write that names nobody; an instance without one has no such field,
    /// so a surface falls back to the author id.
    /// </remarks>
    [JsonPropertyName("maintenance_windows_author_name")]
    public string? MaintenanceWindowsAuthorName { get; set; }

    /// <summary>
    /// Gets or sets the lead times, in minutes, at which this instance announces an
    /// upcoming maintenance window — comma-separated, e.g. <c>"15,5,1"</c>.
    /// </summary>
    /// <remarks>
    /// Null or empty announces nothing, which is the default: a server addressing the
    /// people on it is opt-in. Read by kgsm-scheduler, and inert for a game whose
    /// blueprint declares no <see cref="BroadcastCommand"/>.
    /// </remarks>
    [JsonPropertyName("announce_lead_minutes")]
    public string? AnnounceLeadMinutes { get; set; }

    /// <summary>
    /// Gets or sets what each lead-time announcement says. <c>{minutes}</c> is the lead
    /// being announced and <c>{instance}</c> is the display name; anything else is sent
    /// verbatim.
    /// </summary>
    /// <remarks>
    /// The result of this substitution is then substituted into the game's own broadcast
    /// template — two separate steps with different placeholders and different owners, so
    /// a message carrying <c>{message}</c> is not special here.
    /// </remarks>
    [JsonPropertyName("announce_maintenance_message")]
    public string? AnnounceMaintenanceMessage { get; set; }

    /// <summary>
    /// Gets or sets what is said when an announced maintenance window is abandoned. Players were
    /// told it was coming, so they are told it is not.
    /// </summary>
    [JsonPropertyName("announce_maintenance_cancelled_message")]
    public string? AnnounceMaintenanceCancelledMessage { get; set; }

    /// <summary>
    /// Gets or sets the timezone the instance's maintenance appointments are read in — one instance
    /// has one answer for what time it is. Interval windows ignore it by construction.
    /// Null when KGSM omits <c>timezone</c> — honest unknown, never fabricated.
    /// </summary>
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    /// <summary>
    /// Gets or sets the number of most-recent backups to retain when pruning.
    /// Null when KGSM omits <c>backup_retention</c> — honest unknown, never fabricated.
    /// </summary>
    [JsonPropertyName("backup_retention")]
    public int? BackupRetention { get; set; }

    /// <summary>
    /// Gets or sets whether the watchdog should automatically restart this instance on crash.
    /// Null when KGSM omits <c>crash_restart</c> — consumers default to true (auto-restart on).
    /// When false, any unintentional exit emits instance-crashed but does NOT schedule a restart.
    /// </summary>
    [JsonPropertyName("crash_restart")]
    public bool? CrashRestart { get; set; }

    /// <summary>
    /// Gets or sets the per-instance max consecutive restarts before the watchdog gives up.
    /// Null when KGSM omits <c>crash_max_restarts</c> — consumers fall back to global MaxRetries.
    /// </summary>
    [JsonPropertyName("crash_max_restarts")]
    public int? CrashMaxRestarts { get; set; }

    /// <summary>
    /// Gets or sets the logs redirect pattern for the instance.
    /// </summary>
    [JsonPropertyName("log_file")]
    public string LogFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the regex pattern that indicates successful startup of the instance.
    /// </summary>
    [JsonPropertyName("startup_success_regex")]
    public string StartupSuccessRegex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the regex matched against the game's log output to detect a player
    /// joining (player-presence Increment 2, native detection). Empty when the blueprint
    /// sets no pattern — that detection is disabled (honest unknown, no event invented).
    /// Optional named groups <c>(?&lt;id&gt;…)</c> / <c>(?&lt;name&gt;…)</c> populate the
    /// emitted event's player id / name; at least one must match. Materialized from the
    /// blueprint into the instance config, so the resident supervisor (kgsm-watchdog) reads
    /// it off this Instance to tail a native instance's log. (Containers use the base64 env
    /// form + in-image shim instead; the field is shared but the matching engine differs —
    /// Perl in the container shim, .NET here.)
    /// </summary>
    [JsonPropertyName("player_joined_regex")]
    public string PlayerJoinedRegex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the regex matched against the game's log output to detect a player
    /// leaving — the leave counterpart of <see cref="PlayerJoinedRegex"/>, same rules.
    /// </summary>
    [JsonPropertyName("player_left_regex")]
    public string PlayerLeftRegex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the RCON port for the game server. Null when the blueprint sets no
    /// RCON port — RCON-based player detection is disabled (honest unknown, no event
    /// invented). The watchdog polls this port's <c>players</c> command to detect leaves
    /// when the game server does not log disconnects.
    /// </summary>
    [JsonPropertyName("rcon_port")]
    public int? RconPort { get; set; }

    /// <summary>
    /// Gets or sets the RCON password for authentication. Empty when RCON is not
    /// configured. Stored in plaintext in the instance config; the user must also
    /// configure the game server's own RCON with matching values.
    /// </summary>
    [JsonPropertyName("rcon_password")]
    public string RconPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how often (in seconds) to poll the game server via RCON for
    /// connected players. Null when the blueprint omits it — defaults to 10 seconds
    /// at the poller.
    /// </summary>
    [JsonPropertyName("rcon_poll_interval_seconds")]
    [JsonConverter(typeof(JsonStringToIntConverter))]
    public int? RconPollIntervalSeconds { get; set; }

    /// <summary>
    /// Gets or sets the RCON command to query connected players (game-specific).
    /// Defaults to <c>"players"</c> (Project Zomboid). Other games may use
    /// <c>"status"</c>, <c>"listplayers"</c>, etc.
    /// </summary>
    [JsonPropertyName("rcon_players_command")]
    public string RconPlayersCommand { get; set; } = "players";

    /// <summary>
    /// Gets or sets the pattern that reads one player out of
    /// <see cref="RconPlayersCommand"/>'s output, applied per line, with optional named groups
    /// <c>id</c> and <c>name</c>. Every game words its roster differently — one prints
    /// <c>-Name</c> under a header, another an id and a name in columns — so the shape is carried
    /// as blueprint data and a consumer applies it without knowing which game it is polling.
    /// Empty means the output cannot be read, which disables RCON presence for the instance rather
    /// than guessing a roster out of the server's own prose.
    /// </summary>
    [JsonPropertyName("rcon_players_regex")]
    public string RconPlayersRegex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the resident supervisor (kgsm-watchdog) should UPnP
    /// port-forward this instance — the per-instance gate its <c>UpnpService</c>
    /// reads. <see langword="false"/> (the safe default) when absent from the wire:
    /// the bash engine holds no UPnP logic of its own — it stores and emits this
    /// per-instance flag, and the watchdog owns all port-forwarding behaviour.
    /// Typed here because the watchdog is a live consumer — it must compile
    /// against this Instance.
    /// </summary>
    [JsonPropertyName("enable_port_forwarding")]
    public bool EnablePortForwarding { get; set; } = false;

    /// <summary>
    /// Gets or sets the stored form of the operator-authored server note — base64 of the body
    /// (see <see cref="InstanceNote"/> for why it is encoded). <see cref="string.Empty"/> when the
    /// instance has no note. Read <see cref="NoteBody"/> rather than this; the raw value is exposed
    /// only so a consumer can tell "no note" from "a note that failed to decode".
    /// </summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actor string of whoever last wrote the note (the same actor a surface stamps
    /// onto the config write, so it matches the audit trail). <see cref="string.Empty"/> when the note
    /// was never written through a surface — honest unknown, never a fabricated author.
    /// </summary>
    [JsonPropertyName("note_updated_by")]
    public string NoteUpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC ISO-8601 timestamp of the last note write.
    /// <see cref="string.Empty"/> when unknown.
    /// </summary>
    [JsonPropertyName("note_updated_at")]
    public string NoteUpdatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Gets the decoded server note body, or <see langword="null"/> when the instance has no note.
    /// This is the value surfaces render.
    /// </summary>
    [JsonIgnore]
    public string? NoteBody => InstanceNote.Decode(Note);

    /// <summary>
    /// Gets or sets the level name for the instance.
    /// </summary>
    [JsonPropertyName("level_name")]
    public string LevelName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable arguments for the instance.
    /// </summary>
    [JsonPropertyName("executable_arguments")]
    public string ExecutableArguments { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Steam app ID for the instance.
    /// </summary>
    [JsonPropertyName("steam_app_id")]
    public string SteamAppId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client Steam app ID for launch/connect deeplinks.
    /// </summary>
    [JsonPropertyName("client_steam_app_id")]
    public string ClientSteamAppId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether a Steam account is required for the instance.
    /// </summary>
    [JsonPropertyName("is_steam_account_required")]
    public bool IsSteamAccountRequired { get; set; } = false;

    /// <summary>
    /// Gets or sets the instance's ports as the canonical range-preserving structured form
    /// (<c>[{start,end,protocol}]</c>). KGSM derives this from the UFW-style port spec and emits
    /// it on <c>instances info --json</c>; it replaces the old opaque <c>ports</c> string. Consumers expand it (<see cref="PortMappingExtensions.Expand"/>)
    /// or render it (<see cref="PortMappingExtensions.ToUfwSpec"/>) rather than re-parsing a string.
    /// </summary>
    [JsonPropertyName("ports")]
    public List<PortMapping> Ports { get; set; } = [];

    /// <summary>
    /// Gets or sets whether firewall management is enabled for the instance.
    /// </summary>
    [JsonPropertyName("enable_firewall_management")]
    public bool EnableFirewallManagement { get; set; } = false;

    /// <summary>
    /// Gets or sets the firewall rule file for the instance.
    /// </summary>
    [JsonPropertyName("firewall_rule_file")]
    public string FirewallRuleFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stop command for the instance.
    /// </summary>
    [JsonPropertyName("stop_command")]
    public string StopCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the save command for the instance.
    /// </summary>
    [JsonPropertyName("save_command")]
    public string SaveCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template for disconnecting a player. Empty when the game
    /// declares no kick command, in which case the action is refused rather than
    /// approximated with a different one. See <see cref="ModerationCommand"/> for how
    /// to read the player identity the template asks for.
    /// </summary>
    [JsonPropertyName("kick_command")]
    public string KickCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template for disconnecting a player and blocking them. Empty
    /// when the game declares no ban command.
    /// </summary>
    [JsonPropertyName("ban_command")]
    public string BanCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template for lifting a block. Empty when the game declares no
    /// unban command.
    /// </summary>
    [JsonPropertyName("unban_command")]
    public string UnbanCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template for announcing a message to everyone connected.
    /// Empty when the game declares no broadcast command, in which case the
    /// announcement is refused rather than approximated with a different one.
    /// </summary>
    /// <remarks>
    /// The template carries exactly one <c>{message}</c> placeholder, and may consist
    /// of nothing else — a console that treats any bare line as chat needs no verb in
    /// front of the text. See <see cref="Models.BroadcastCommand"/> for reading whether
    /// the action is supported at all.
    /// <para>
    /// This is the STDIN console template. A game whose broadcast exists only over RCON
    /// or an in-game admin console declares none here, so an empty value means "not on
    /// this channel" rather than "this game cannot be announced to".
    /// </para>
    /// </remarks>
    [JsonPropertyName("broadcast_command")]
    public string BroadcastCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the save command timeout in seconds for the instance.
    /// </summary>
    [JsonPropertyName("save_command_timeout_seconds")]
    [JsonConverter(typeof(JsonStringToIntConverter))]
    public int SaveCommandTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the stop command timeout in seconds for the instance.
    /// </summary>
    [JsonPropertyName("stop_command_timeout_seconds")]
    [JsonConverter(typeof(JsonStringToIntConverter))]
    public int StopCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether backups are compressed for the instance.
    /// </summary>
    [JsonPropertyName("compress_backups")]
    public bool CompressBackups { get; set; } = false;

    /// <summary>
    /// Gets or sets whether command shortcuts are enabled for the instance.
    /// </summary>
    [JsonPropertyName("enable_command_shortcuts")]
    public bool EnableCommandShortcuts { get; set; } = false;

    /// <summary>
    /// Gets or sets the command shortcut file for the instance.
    /// </summary>
    [JsonPropertyName("command_shortcut_file")]
    public string CommandShortcutFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the blueprint the instance was made from — <c>factorio</c>, not
    /// <c>factorio.bp.yaml</c>.
    /// </summary>
    /// <remarks>
    /// The engine states this directly for an instance whose library is away, where
    /// <see cref="BlueprintFile"/> is one of the many things that cannot be read: the name comes
    /// out of the instance registry, which is on this host rather than on the absent disk. When it
    /// is not stated, it is derived from the file path — a unified blueprint is
    /// <c>&lt;name&gt;.bp.yaml</c>, so the compound suffix comes off as a unit.
    /// </remarks>
    [JsonPropertyName("blueprint")]
    public string Blueprint
    {
        get => string.IsNullOrEmpty(_blueprint) ? DeriveBlueprintName(BlueprintFile) : _blueprint;
        set => _blueprint = value;
    }

    private string? _blueprint;

    private static readonly string[] BlueprintSuffixes = [".bp.yaml", ".bp.yml"];

    private static string DeriveBlueprintName(string blueprintFile)
    {
        if (string.IsNullOrEmpty(blueprintFile)) return string.Empty;

        string file = Path.GetFileName(blueprintFile);
        foreach (string suffix in BlueprintSuffixes)
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return file[..^suffix.Length];

        return Path.GetFileNameWithoutExtension(file);
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return $"Instance: {Name}, " +
               $"Runtime: {Runtime}, " +
               $"Platform: {Platform}, " +
               $"WorkingDir: {WorkingDir}, " +
               $"InstallDir: {InstallDir}, " +
               $"LibraryDir: {LibraryDir}, " +
               $"Library: {Library}, " +
               $"LibraryState: {LibraryState}, " +
               $"LogsDir: {LogsDir}, " +
               $"InstallDateTime: {InstallDateTime}, " +
               $"BlueprintFile: {BlueprintFile}, " +
               $"ManagementFile: {ManagementFile}, " +
               $"PidFile: {PidFile}, " +
               $"SocketFile: {SocketFile}, " +
               $"FirewallRuleFile: {FirewallRuleFile}";
    }
}
