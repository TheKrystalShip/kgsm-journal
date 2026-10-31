using System.Text.Json;
using TheKrystalShip.KGSM.Core.Models.Enums;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Pins the <see cref="Instance"/> wire shape from <c>instances info &lt;name&gt; --json</c>, focused
/// on <see cref="Instance.Runtime"/>. With systemd removed, <c>runtime</c> (native|container) is the
/// SOLE supervision discriminator (kgsm-watchdog gates on it), so its binding is load-bearing —
/// and the property carries no <c>[JsonPropertyName]</c>, relying on case-insensitive matching of
/// KGSM's lowercase <c>runtime</c> field. These guard that, plus the placement fields an instance
/// reports whether or not its library is mounted, and that the removed legacy fields are tolerated
/// rather than thrown on.
/// </summary>
public class InstanceDeserializationTests
{
    private readonly Mock<IProcessRunner> _processRunner = new();
    private readonly Mock<ILogger<KgsmCommandExecutor>> _logger = new();

    private KgsmCommandExecutor Create() =>
        new(_processRunner.Object,
            new KgsmOptions { KgsmPath = "/opt/kgsm/kgsm.sh", Timeouts = new KgsmTimeoutOptions() },
            _logger.Object);

    private void StubProcessOutput(string stdout) =>
        _processRunner
            .Setup(r => r.Execute(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string[]>()))
            .Returns(new ProcessResult(0, stdout, string.Empty));

    private Instance? Info() =>
        Create().ExecuteForJson<Instance>(["instances", "info", "7dtd", "--json"]);

    [Theory]
    [InlineData("native", InstanceRuntime.Native)]
    [InlineData("container", InstanceRuntime.Container)]
    [InlineData("NATIVE", InstanceRuntime.Native)]       // KGSM emits lowercase; binding is case-insensitive
    [InlineData("Container", InstanceRuntime.Container)]
    public void Runtime_binds_case_insensitively_from_the_wire_field(string wire, InstanceRuntime expected)
    {
        StubProcessOutput($$"""{"name":"7dtd","runtime":"{{wire}}"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Runtime);
    }

    [Fact]
    public void Runtime_is_unknown_when_the_field_is_absent()
    {
        // The engine omits it for an instance whose library is not mounted — the value lives in the
        // instance's own config, on the disk that is away. Reading that as Native would send a
        // consumer to the watchdog for what might be a container.
        StubProcessOutput("""{"name":"7dtd"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Null(result!.Runtime);
    }

    [Fact]
    public void LibraryState_binds_the_three_states_the_engine_measures()
    {
        foreach ((string wire, InstanceLibraryState expected) in new[]
        {
            ("online", InstanceLibraryState.Online),
            ("offline", InstanceLibraryState.Offline),
            ("unregistered", InstanceLibraryState.Unregistered),
        })
        {
            StubProcessOutput($$"""{"name":"7dtd","library_state":"{{wire}}"}""");

            Instance? result = Info();

            Assert.NotNull(result);
            Assert.Equal(expected, result!.LibraryState);
        }
    }

    [Fact]
    public void LibraryState_is_unknown_on_an_engine_that_does_not_report_it()
    {
        StubProcessOutput("""{"name":"7dtd"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Null(result!.LibraryState);
    }

    [Fact]
    public void Blueprint_is_derived_from_the_unified_blueprint_file_name()
    {
        // "<name>.bp.yaml" — the compound suffix comes off as a unit, not one extension at a time.
        StubProcessOutput("""{"name":"7dtd","blueprint_file":"/opt/kgsm/blueprints/factorio.bp.yaml"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("factorio", result!.Blueprint);
    }

    [Fact]
    public void Blueprint_comes_from_the_engine_when_the_library_is_offline()
    {
        // The whole of what an offline instance reports: no blueprint_file, because that path is on
        // the absent disk. The name is on this host, in the instance registry.
        StubProcessOutput(
            """
            {"name":"7dtd","blueprint":"7daystodie","working_dir":"/mnt/ssd/instances/7daystodie/7dtd",
             "library":"ssd","library_dir":"/mnt/ssd","library_state":"offline"}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("7daystodie", result!.Blueprint);
        Assert.Equal(InstanceLibraryState.Offline, result.LibraryState);
        Assert.Equal("ssd", result.Library);
        Assert.Equal("/mnt/ssd", result.LibraryDir);
        Assert.Null(result.Runtime);
    }

    [Fact]
    public void CgroupPath_binds_from_the_wire_field()
    {
        // KGSM emits the derived native cgroup directory; the monitor reads it to sample
        // cgroup counters directly. Binds via [JsonPropertyName("cgroup_path")].
        StubProcessOutput("""{"name":"7dtd","runtime":"native","cgroup_path":"/sys/fs/cgroup/kgsm.slice/7dtd"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("/sys/fs/cgroup/kgsm.slice/7dtd", result!.CgroupPath);
    }

    [Fact]
    public void CgroupPath_defaults_to_empty_when_absent()
    {
        // Older KGSM (and container instances) emit no cgroup_path — it must deserialize to
        // empty, the signal the monitor uses to fall back to its /proc-tree probe.
        StubProcessOutput("""{"name":"7dtd","runtime":"native"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result!.CgroupPath);
    }

    [Fact]
    public void PlayerPresenceRegexes_bind_from_the_wire_fields()
    {
        // KGSM materializes the blueprint's player_joined_regex / player_left_regex into the
        // instance config, which the generic instances-info JSON dump emits. The watchdog reads
        // these off the native Instance to tail its log. Bind via [JsonPropertyName("player_*_regex")].
        StubProcessOutput(
            """{"name":"factorio-01","runtime":"native","player_joined_regex":"\\[JOIN\\] (?<name>.+) joined","player_left_regex":"\\[LEAVE\\] (?<name>.+) left"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("\\[JOIN\\] (?<name>.+) joined", result!.PlayerJoinedRegex);
        Assert.Equal("\\[LEAVE\\] (?<name>.+) left", result.PlayerLeftRegex);
    }

    [Fact]
    public void PlayerPresenceRegexes_default_to_empty_when_absent()
    {
        // No blueprint pattern set (or a pre-1.20.0 KGSM) → empty, the watchdog's signal that
        // native detection is disabled for this instance (honest unknown, no event invented).
        StubProcessOutput("""{"name":"7dtd","runtime":"native"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result!.PlayerJoinedRegex);
        Assert.Equal(string.Empty, result.PlayerLeftRegex);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    public void EnablePortForwarding_binds_from_the_stringly_bool_wire(string wire, bool expected)
    {
        // KGSM's instances-info dump renders every config value as a STRING (jq -R), so the UPnP gate
        // arrives as "true"/"false" — NOT a JSON bool. The global JsonStringToBoolConverter coerces it
        // (case-insensitive), the same path enable_firewall_management already rides. The watchdog reads
        // this off the spec (GetInstanceInfo) to gate upnpc — if it didn't bind, UPnP could never be
        // enabled. Binds via [JsonPropertyName("enable_port_forwarding")].
        StubProcessOutput(
            "{\"name\":\"factorio-01\",\"runtime\":\"native\",\"enable_port_forwarding\":\"" + wire + "\"}");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(expected, result!.EnablePortForwarding);
    }

    [Fact]
    public void EnablePortForwarding_defaults_to_false_when_absent()
    {
        // An instance whose config predates the gate (or a pre-restore KGSM) emits no key → false =
        // inert (the safe default; the watchdog runs no upnpc until the operator enables it).
        StubProcessOutput("""{"name":"7dtd","runtime":"native"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.False(result!.EnablePortForwarding);
    }

    [Fact]
    public void Legacy_systemd_and_lifecycle_manager_fields_are_ignored_not_thrown_on()
    {
        // Older KGSM still emits lifecycle_manager / enable_systemd / systemd_* — their properties were
        // removed, so they must deserialize as unmapped (skipped), not throw and collapse the read.
        StubProcessOutput("""
            {"name":"7dtd","runtime":"native","lifecycle_manager":"systemd","enable_systemd":"true","systemd_service_file":"/etc/systemd/system/7dtd.service","systemd_socket_file":"/etc/systemd/system/7dtd.socket"}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("7dtd", result!.Name);
        Assert.Equal(InstanceRuntime.Native, result.Runtime);
    }

    [Fact]
    public void Ports_bind_from_the_structured_array_real_factorio_shape()
    {
        // Captured verbatim from `kgsm instances info factorio-test --json | jq -c .ports`:
        // a proto-less single port expands to one tcp + one udp mapping, each start==end.
        StubProcessOutput("""
            {"name":"7dtd","runtime":"native","ports":[{"start":34197,"end":34197,"protocol":"tcp"},{"start":34197,"end":34197,"protocol":"udp"}]}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(
            [new PortMapping { Start = 34197, End = 34197, Protocol = "tcp" },
             new PortMapping { Start = 34197, End = 34197, Protocol = "udp" }],
            result!.Ports);
    }

    [Fact]
    public void Ports_preserve_ranges_as_a_single_mapping()
    {
        // A UFW range stays ONE {start,end} mapping (range-preserving) — not unrolled on the wire.
        StubProcessOutput("""
            {"name":"7dtd","runtime":"native","ports":[{"start":27015,"end":27020,"protocol":"udp"}]}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        PortMapping only = Assert.Single(result!.Ports);
        Assert.Equal(27015, only.Start);
        Assert.Equal(27020, only.End);
        Assert.Equal("udp", only.Protocol);
    }

    [Fact]
    public void Ports_default_to_empty_list_when_absent()
    {
        // Containers (Docker owns ports) and older KGSM emit no `ports` — must be an empty list,
        // never null, so consumers can enumerate without a null check.
        StubProcessOutput("""{"name":"7dtd","runtime":"native"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.NotNull(result!.Ports);
        Assert.Empty(result.Ports);
    }

    [Fact]
    public void Ports_start_end_also_bind_from_stringly_numbers()
    {
        // Defensive: even if start/end arrive as KGSM's stringly scalars, the global
        // string->int coercion binds them — either wire shape works.
        StubProcessOutput("""
            {"name":"7dtd","runtime":"native","ports":[{"start":"80","end":"80","protocol":"tcp"}]}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        PortMapping only = Assert.Single(result!.Ports);
        Assert.Equal(80, only.Start);
        Assert.Equal(80, only.End);
    }

    [Fact]
    public void Moderation_templates_bind_from_the_snake_case_wire_fields()
    {
        // `instances info --json` serializes the whole instance .config.ini, so these
        // arrive under their INI key names.
        StubProcessOutput("""
            {"name":"romestead","runtime":"native","kick_command":"kick {ip}","ban_command":"ban {ip}","unban_command":"unban {ip}"}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("kick {ip}", result!.KickCommand);
        Assert.Equal("ban {ip}", result.BanCommand);
        Assert.Equal("unban {ip}", result.UnbanCommand);

        // The template is what a caller reads the identity contract out of.
        Assert.True(ModerationCommand.TryGetTargetKind(result.KickCommand, out ModerationTargetKind kind));
        Assert.Equal(ModerationTargetKind.Ip, kind);
    }

    [Fact]
    public void Moderation_templates_are_empty_when_the_game_declares_none()
    {
        // An instance created from a blueprint with no moderation fields. Empty means
        // unsupported — the action is refused, never approximated with another command.
        StubProcessOutput("""{"name":"7dtd","runtime":"native"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result!.KickCommand);
        Assert.False(ModerationCommand.IsSupported(result.KickCommand));
        Assert.False(ModerationCommand.IsSupported(result.BanCommand));
        Assert.False(ModerationCommand.IsSupported(result.UnbanCommand));
    }

    // --- display_name : the label, bound off the same info JSON as everything else -------------

    [Fact]
    public void DisplayName_binds_from_the_wire_field()
    {
        StubProcessOutput("""{"name":"factorio-42","display_name":"Weekend Server"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("Weekend Server", result!.DisplayName);
        // The id is untouched by the label — everything that keys on an instance keys on this.
        Assert.Equal("factorio-42", result.Name);
    }

    [Theory]
    // The engine escapes these on the way into the config and unescapes them on the way back out, so
    // what reaches the JSON is the text somebody typed. Nothing here needs handling on this side —
    // these are pinned because a label is the one instance field written from free user input.
    [InlineData("Ana's \"Best\" Server")]
    [InlineData(@"C:\path\to\nowhere")]
    [InlineData("Sûper Ćool 🎮 Server")]
    [InlineData("cost: $100 `uname`")]
    public void DisplayName_round_trips_text_the_engine_had_to_escape(string label)
    {
        StubProcessOutput(JsonSerializer.Serialize(
            new Dictionary<string, string> { ["name"] = "factorio-42", ["display_name"] = label }));

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(label, result!.DisplayName);
    }

    [Fact]
    public void DisplayName_reads_as_the_id_when_the_engine_states_none()
    {
        // The one case: an instance whose library is offline, whose config cannot be read — the
        // engine will not invent a label it cannot see, and the honest label for an instance without
        // one is its id. A blank here would render as a nameless row.
        StubProcessOutput("""{"name":"factorio-42","library_state":"offline"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("factorio-42", result!.DisplayName);
    }

    [Fact]
    public void DisplayName_reads_as_the_id_when_the_label_was_cleared()
    {
        StubProcessOutput("""{"name":"factorio-42","display_name":""}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("factorio-42", result!.DisplayName);
    }

    [Fact]
    public void An_author_without_a_name_binds_the_name_as_null()
    {
        StubProcessOutput("""{"name":"factorio-42","maintenance_windows_author":"usr_alice"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal("usr_alice", result!.MaintenanceWindowsAuthor);
        Assert.Null(result.MaintenanceWindowsAuthorName);
    }

    [Fact]
    public void Maintenance_keys_bind_from_the_wire()
    {
        // The whole maintenance surface an instance carries: the packed windows, the timezone the
        // appointments in them are read in, and what the server says before a window it announces.
        StubProcessOutput("""
            {"name":"factorio-42",
             "maintenance_windows":"daily@05:00/backup;weekly.sun@04:00/backup,update,restart",
             "maintenance_windows_author":"usr_alice",
             "maintenance_windows_author_name":"Alice Doe",
             "timezone":"Europe/Madrid",
             "backup_retention":"5",
             "announce_lead_minutes":"15,5,1",
             "announce_maintenance_message":"{instance} is {reason} in {minutes} minutes",
             "announce_maintenance_cancelled_message":"{instance} is staying up"}
            """);

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Equal(
            "daily@05:00/backup;weekly.sun@04:00/backup,update,restart",
            result!.MaintenanceWindows);
        Assert.Equal("usr_alice", result.MaintenanceWindowsAuthor);
        Assert.Equal("Alice Doe", result.MaintenanceWindowsAuthorName);
        Assert.Equal("Europe/Madrid", result.Timezone);
        Assert.Equal(5, result.BackupRetention);
        Assert.Equal("15,5,1", result.AnnounceLeadMinutes);
        Assert.Equal("{instance} is {reason} in {minutes} minutes", result.AnnounceMaintenanceMessage);
        Assert.Equal("{instance} is staying up", result.AnnounceMaintenanceCancelledMessage);
    }

    [Fact]
    public void An_instance_with_no_maintenance_reports_null_rather_than_an_empty_schedule()
    {
        StubProcessOutput("""{"name":"factorio-42"}""");

        Instance? result = Info();

        Assert.NotNull(result);
        Assert.Null(result!.MaintenanceWindows);
        Assert.Empty(TheKrystalShip.KGSM.Core.Scheduling.MaintenanceWindowParser.Parse(result.MaintenanceWindows));
    }
}
