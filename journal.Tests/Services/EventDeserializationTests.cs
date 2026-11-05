using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Guards the kgsm → kgsm-lib event contract at the wire boundary.
///
/// Event names and payload shapes are maintained by hand in two places that
/// must agree: kgsm's <c>EVENT_CONFIGS</c> registry (bash) and the C#
/// <see cref="KgsmEventCatalog"/> + <see cref="EventDataBase"/> types. They had drifted
/// — <c>instance-restarted</c> and the <c>*-failed</c> events were emitted by
/// kgsm but had no C# type, so they were dropped as "Unknown event type". These
/// tests pin (1) the real wire payloads for the newly-aligned events, captured
/// verbatim from <c>_build_event_payload</c>, and (2) the AOT invariant that
/// every event type is registered in <see cref="JournalJsonContext"/> — an
/// unregistered type throws at runtime on the reflection-free deserialize path.
/// </summary>
public class EventDeserializationTests
{
    // The envelope's payload arrives as the registered type its event names.
    private static (string EventType, KgsmEventDataBase? Data) DeserializeAny(
        string wireJson, Type targetType)
    {
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(wireJson, JournalJsonContext.Default.EventWrapper);
        Assert.NotNull(wrapper);
        Assert.IsType(targetType, wrapper!.Data);

        return (wrapper.EventType, wrapper.Data);
    }

    private static (string EventType, EventDataBase? Data) Deserialize(
        string wireJson, Type targetType)
    {
        (string eventType, KgsmEventDataBase? data) = DeserializeAny(wireJson, targetType);
        return (eventType, data as EventDataBase);
    }

    // Captured from an older kgsm `_build_event_payload instance_restarted 7dtd standalone`. The
    // `LifecycleManager` field has since been removed from KGSM's payloads; it is retained here on
    // purpose to prove the lib tolerates (ignores) the legacy field rather than throwing on it.
    private const string RestartedWireJson = """
        {"EventType":"server.restarted","Data":{"InstanceName":"7dtd","LifecycleManager":"standalone"},"Timestamp":"2026-06-11T21:00:43Z","Hostname":"hotrod","KGSMVersion":"unknown"}
        """;

    // Captured verbatim from kgsm `_build_event_payload instance_download_failed 7dtd`.
    private const string DownloadFailedWireJson = """
        {"EventType":"server.download.failed","Data":{"InstanceName":"7dtd"},"Timestamp":"2026-06-11T21:00:43Z","Hostname":"hotrod","KGSMVersion":"unknown"}
        """;

    [Fact]
    public void RestartedEvent_Deserializes_IgnoringLegacyLifecycleManager()
    {
        (string eventType, EventDataBase? data) = Deserialize(
            RestartedWireJson, typeof(InstanceRestartedData));

        Assert.Equal("server.restarted", eventType);
        var restarted = Assert.IsType<InstanceRestartedData>(data);
        Assert.Equal("7dtd", restarted.InstanceName);
        // The legacy `LifecycleManager` field in the payload is unmapped and silently ignored
        // (the property was removed) — deserialization must not throw on it.
    }

    [Fact]
    public void DownloadFailedEvent_DeserializesWithInstanceNameOnly()
    {
        (string eventType, EventDataBase? data) = Deserialize(
            DownloadFailedWireJson, typeof(InstanceDownloadFailedData));

        Assert.Equal("server.download.failed", eventType);
        var failed = Assert.IsType<InstanceDownloadFailedData>(data);
        Assert.Equal("7dtd", failed.InstanceName);
    }

    // Captured verbatim from the journal after a live `kgsm instances check-update starbound --emit`.
    // Origin is null because a bare CLI call declares no surface — the scheduler's sweep stamps one.
    private const string UpdateAvailableWireJson = """
        {"EventType":"server.update.available","Data":{"InstanceName":"starbound","CurrentVersion":"16000000","LatestVersion":"16302742"},"Timestamp":"2026-08-10T11:17:48Z","Actor":"heisen","Origin":null,"Hostname":"hotrod","KGSMVersion":"3.12.0-rc5"}
        """;

    [Fact]
    public void UpdateAvailableEvent_DeserializesBothVersions()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(UpdateAvailableWireJson, typeof(InstanceUpdateAvailableData));

        Assert.Equal("server.update.available", eventType);
        var available = Assert.IsType<InstanceUpdateAvailableData>(data);
        Assert.Equal("starbound", available.InstanceName);

        // Both versions, because "an update is available" without saying from what to what is not
        // something a surface can announce or an audit row can be read back from.
        Assert.Equal("16000000", available.CurrentVersion);
        Assert.Equal("16302742", available.LatestVersion);
    }

    // Models the kgsm `_build_event_payload instance_crashed 7dtd 139 2` wire shape: the
    // watchdog crash-restart event, stamped Actor=system / Origin=system (autonomous engine
    // action), carrying the exit code + restart-attempt count as strings (the jq --arg wire
    // form). Reconstructed from the payload builder; the BashEventRegistry conformance test
    // pins the event name against the real sibling kgsm.
    private const string CrashedWireJson = """
        {"EventType":"server.crashed","Data":{"InstanceName":"7dtd","ExitCode":"139","Restarts":"2"},"Timestamp":"2026-06-15T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"unknown"}
        """;

    // The give-up event: the supervisor exhausted its retries. ExitCode is the literal
    // "unknown" here — the respawn could not read a code — never a fabricated 0.
    private const string FailedWireJson = """
        {"EventType":"server.crash.exhausted","Data":{"InstanceName":"7dtd","ExitCode":"unknown","Restarts":"5"},"Timestamp":"2026-06-15T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"unknown"}
        """;

    [Fact]
    public void CrashedEvent_DeserializesExitCodeAndRestarts_WithSystemProvenance()
    {
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(CrashedWireJson, JournalJsonContext.Default.EventWrapper);
        Assert.NotNull(wrapper);
        // Autonomous engine action: who = system, surface = system.
        Assert.Equal("system", wrapper!.Actor);
        Assert.Equal("system", wrapper.Origin);

        (string eventType, EventDataBase? data) =
            Deserialize(CrashedWireJson, typeof(InstanceCrashedData));

        Assert.Equal("server.crashed", eventType);
        var crashed = Assert.IsType<InstanceCrashedData>(data);
        Assert.Equal("7dtd", crashed.InstanceName);
        Assert.Equal("139", crashed.ExitCode);
        Assert.Equal("2", crashed.Restarts);
    }

    [Fact]
    public void FailedEvent_DeserializesWithUnknownExitCode_NeverFabricated()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(FailedWireJson, typeof(InstanceFailedData));

        Assert.Equal("server.crash.exhausted", eventType);
        var failed = Assert.IsType<InstanceFailedData>(data);
        Assert.Equal("7dtd", failed.InstanceName);
        // Honest unknown — the unreadable exit code is "unknown", not a fabricated code.
        Assert.Equal("unknown", failed.ExitCode);
        Assert.Equal("5", failed.Restarts);
    }

    // Models the kgsm `_build_event_payload instance_ports_opened factorio-01
    // '34197/udp|27015:27020/tcp'` wire shape: the firewall audit event the kgsm
    // command layer emits after the kgsm-firewall authority opens the ports. Data.Ports
    // is the canonical STRUCTURED array (range-preserving [{start,end,protocol}], the
    // same shape `instances info --json` emits), built via jq --argjson — never an
    // opaque UFW string. Stamped with the caller's actor/origin (here a kgsm-api emit).
    private const string PortsOpenedWireJson = """
        {"EventType":"network.ports.opened","Data":{"InstanceName":"factorio-01","Ports":[{"start":34197,"end":34197,"protocol":"udp"},{"start":27015,"end":27020,"protocol":"tcp"}]},"Timestamp":"2026-06-16T08:00:00Z","Actor":"discord:tester","Origin":"api","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    // The close event with a single proto-less-expanded port (one entry here for variety).
    private const string PortsClosedWireJson = """
        {"EventType":"network.ports.closed","Data":{"InstanceName":"factorio-01","Ports":[{"start":7777,"end":7777,"protocol":"tcp"}]},"Timestamp":"2026-06-16T08:00:00Z","Actor":"system","Origin":"api","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    [Fact]
    public void PortsOpenedEvent_DeserializesStructuredRangePreservingPorts()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PortsOpenedWireJson, typeof(InstancePortsOpenedData));

        Assert.Equal("network.ports.opened", eventType);
        var opened = Assert.IsType<InstancePortsOpenedData>(data);
        Assert.Equal("factorio-01", opened.InstanceName);

        // Ports bind from the structured wire array, NOT an opaque string — and the
        // tcp range is preserved (start != end), never expanded.
        Assert.Equal(2, opened.Ports.Count);
        Assert.Equal(new PortMapping { Start = 34197, End = 34197, Protocol = "udp" }, opened.Ports[0]);
        Assert.Equal(new PortMapping { Start = 27015, End = 27020, Protocol = "tcp" }, opened.Ports[1]);
    }

    [Fact]
    public void PortsClosedEvent_DeserializesStructuredPorts()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PortsClosedWireJson, typeof(InstancePortsClosedData));

        Assert.Equal("network.ports.closed", eventType);
        var closed = Assert.IsType<InstancePortsClosedData>(data);
        Assert.Equal("factorio-01", closed.InstanceName);
        Assert.Single(closed.Ports);
        Assert.Equal(new PortMapping { Start = 7777, End = 7777, Protocol = "tcp" }, closed.Ports[0]);
    }

    // The watchdog's UPnP-open audit event — DISTINCT from instance_ports_opened (router NAT
    // forward, not a ufw rule). Emitted by the resident supervisor after upnpc exits 0, stamped
    // Actor=system / Origin=system (an autonomous daemon action). Same structured Ports shape.
    private const string UpnpOpenedWireJson = """
        {"EventType":"network.upnp.opened","Data":{"InstanceName":"factorio-01","Ports":[{"start":34197,"end":34197,"protocol":"udp"},{"start":27015,"end":27020,"protocol":"tcp"}]},"Timestamp":"2026-06-20T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    private const string UpnpClosedWireJson = """
        {"EventType":"network.upnp.closed","Data":{"InstanceName":"factorio-01","Ports":[{"start":7777,"end":7777,"protocol":"udp"}]},"Timestamp":"2026-06-20T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    // The sweep's re-assert: the router dropped a forward while the instance kept running. Ports
    // carries only the subset that was missing, so a partial restoration deserializes as exactly
    // what was restored.
    private const string UpnpReassertedWireJson = """
        {"EventType":"network.upnp.reasserted","Data":{"InstanceName":"factorio-01","Ports":[{"start":27015,"end":27020,"protocol":"tcp"}]},"Timestamp":"2026-06-20T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    [Fact]
    public void UpnpOpenedEvent_DeserializesStructuredRangePreservingPorts()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(UpnpOpenedWireJson, typeof(InstanceUpnpOpenedData));

        Assert.Equal("network.upnp.opened", eventType);
        var opened = Assert.IsType<InstanceUpnpOpenedData>(data);
        Assert.Equal("factorio-01", opened.InstanceName);

        // Range preserved (start != end), never pre-expanded — same canonical shape as the
        // firewall ports event, a different (router) fact.
        Assert.Equal(2, opened.Ports.Count);
        Assert.Equal(new PortMapping { Start = 34197, End = 34197, Protocol = "udp" }, opened.Ports[0]);
        Assert.Equal(new PortMapping { Start = 27015, End = 27020, Protocol = "tcp" }, opened.Ports[1]);
    }

    [Fact]
    public void UpnpClosedEvent_DeserializesStructuredPorts()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(UpnpClosedWireJson, typeof(InstanceUpnpClosedData));

        Assert.Equal("network.upnp.closed", eventType);
        var closed = Assert.IsType<InstanceUpnpClosedData>(data);
        Assert.Equal("factorio-01", closed.InstanceName);
        Assert.Single(closed.Ports);
        Assert.Equal(new PortMapping { Start = 7777, End = 7777, Protocol = "udp" }, closed.Ports[0]);
    }

    [Fact]
    public void UpnpReassertedEvent_DeserializesOnlyTheRestoredSubset()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(UpnpReassertedWireJson, typeof(InstanceUpnpReassertedData));

        Assert.Equal("network.upnp.reasserted", eventType);
        var reasserted = Assert.IsType<InstanceUpnpReassertedData>(data);
        Assert.Equal("factorio-01", reasserted.InstanceName);

        // The instance also forwards 34197/udp; only the tcp range was missing, so only it is
        // reported. A re-assert claiming the whole configured set would overstate what changed.
        Assert.Single(reasserted.Ports);
        Assert.Equal(new PortMapping { Start = 27015, End = 27020, Protocol = "tcp" }, reasserted.Ports[0]);
    }

    // Models the kgsm `_build_event_payload instance_player_joined factorio-01 76561198000000000 haru`
    // wire shape: Data.PlayerId / Data.PlayerName are the out-of-band nullable params (rendered to JSON
    // null when empty by the builder — never an empty string). Forwarded by the watchdog from a
    // container's in-image shim → stamped Actor=system / Origin=system (an autonomous observation).
    private const string PlayerJoinedWireJson = """
        {"EventType":"player.joined","Data":{"InstanceName":"factorio-01","PlayerId":"76561198000000000","PlayerName":"haru"},"Timestamp":"2026-06-20T08:00:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    // The leave event with a NAME-ONLY source: PlayerId is JSON null (the source gave no stable id) —
    // surfaced honestly as null, never a fabricated id. The at-least-one-non-null rule is the shim's job.
    private const string PlayerLeftWireJson = """
        {"EventType":"player.left","Data":{"InstanceName":"factorio-01","PlayerId":null,"PlayerName":"haru"},"Timestamp":"2026-06-20T08:05:00Z","Actor":"system","Origin":"system","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    [Fact]
    public void PlayerJoinedEvent_DeserializesIdAndName_WithSystemProvenance()
    {
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(PlayerJoinedWireJson, JournalJsonContext.Default.EventWrapper);
        Assert.NotNull(wrapper);
        // Autonomous observation forwarded by the watchdog: who = system, surface = system.
        Assert.Equal("system", wrapper!.Actor);
        Assert.Equal("system", wrapper.Origin);

        (string eventType, EventDataBase? data) =
            Deserialize(PlayerJoinedWireJson, typeof(InstancePlayerJoinedData));

        Assert.Equal("player.joined", eventType);
        var joined = Assert.IsType<InstancePlayerJoinedData>(data);
        Assert.Equal("factorio-01", joined.InstanceName);
        Assert.Equal("76561198000000000", joined.PlayerId);
        Assert.Equal("haru", joined.PlayerName);
    }

    [Fact]
    public void PlayerLeftEvent_DeserializesNameOnly_NullIdNeverFabricated()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PlayerLeftWireJson, typeof(InstancePlayerLeftData));

        Assert.Equal("player.left", eventType);
        var left = Assert.IsType<InstancePlayerLeftData>(data);
        Assert.Equal("factorio-01", left.InstanceName);
        // Name-only source: the id is honestly null, not a fabricated value.
        Assert.Null(left.PlayerId);
        Assert.Equal("haru", left.PlayerName);
    }

    // Captured verbatim from the live journal on hotrod (/var/lib/kgsm/events/*.ndjson)
    // after `kgsm instances kick|ban|unban romestead 95.19.50.122` against a running
    // server. Target is the identity the operator supplied — an IP here, because
    // romestead's blueprint declares `kick {ip}`; Command is what the engine resolved
    // and actually delivered.
    private const string PlayerKickedWireJson = """
        {"EventType":"player.kicked","Data":{"InstanceName":"romestead","Target":"95.19.50.122","Command":"kick 95.19.50.122"},"Timestamp":"2026-08-04T20:31:00Z","Actor":"heisen","Origin":"cli","Hostname":"hotrod","KGSMVersion":"3.7.0-rc1"}
        """;

    private const string PlayerBannedWireJson = """
        {"EventType":"player.banned","Data":{"InstanceName":"romestead","Target":"95.19.50.122","Command":"ban 95.19.50.122"},"Timestamp":"2026-08-04T20:31:02Z","Actor":"heisen","Origin":"cli","Hostname":"hotrod","KGSMVersion":"3.7.0-rc1"}
        """;

    private const string PlayerUnbannedWireJson = """
        {"EventType":"player.unbanned","Data":{"InstanceName":"romestead","Target":"95.19.50.122","Command":"unban 95.19.50.122"},"Timestamp":"2026-08-04T20:31:04Z","Actor":"heisen","Origin":"cli","Hostname":"hotrod","KGSMVersion":"3.7.0-rc1"}
        """;

    [Fact]
    public void PlayerKickedEvent_CarriesTargetAndResolvedCommand()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PlayerKickedWireJson, typeof(InstancePlayerKickedData));

        Assert.Equal("player.kicked", eventType);
        var kicked = Assert.IsType<InstancePlayerKickedData>(data);
        Assert.Equal("romestead", kicked.InstanceName);
        Assert.Equal("95.19.50.122", kicked.Target);
        Assert.Equal("kick 95.19.50.122", kicked.Command);
    }

    [Fact]
    public void PlayerBannedEvent_CarriesTargetAndResolvedCommand()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PlayerBannedWireJson, typeof(InstancePlayerBannedData));

        Assert.Equal("player.banned", eventType);
        var banned = Assert.IsType<InstancePlayerBannedData>(data);
        Assert.Equal("95.19.50.122", banned.Target);
        Assert.Equal("ban 95.19.50.122", banned.Command);
    }

    [Fact]
    public void PlayerUnbannedEvent_CarriesTargetAndResolvedCommand()
    {
        (string eventType, EventDataBase? data) =
            Deserialize(PlayerUnbannedWireJson, typeof(InstancePlayerUnbannedData));

        Assert.Equal("player.unbanned", eventType);
        var unbanned = Assert.IsType<InstancePlayerUnbannedData>(data);
        Assert.Equal("95.19.50.122", unbanned.Target);
        Assert.Equal("unban 95.19.50.122", unbanned.Command);
    }

    [Fact]
    public void ModerationEvents_CarryOperatorProvenance_NotSystem()
    {
        // Unlike the player join/leave pair (autonomous observations stamped system),
        // a moderation event is a human action and must stay attributable to whoever
        // caused it.
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(PlayerBannedWireJson, JournalJsonContext.Default.EventWrapper);

        Assert.NotNull(wrapper);
        Assert.Equal("heisen", wrapper!.Actor);
        Assert.Equal("cli", wrapper.Origin);
    }

    // Models the kgsm `_build_event_payload` wire shape after the actor/timestamp
    // enrichment (reconstructed from a captured emit; JSON is whitespace/order-
    // insensitive): the envelope now carries a top-level Actor alongside Timestamp.
    private const string EnrichedWireJson = """
        {"EventType":"server.started","Data":{"InstanceName":"factorio-01"},"Timestamp":"2026-06-14T15:39:58Z","Actor":"discord:tester","Hostname":"hotrod","KGSMVersion":"3.0.0"}
        """;

    [Fact]
    public void EventWrapper_SurfacesEnvelopeMetadata_FromEnrichedWireJson()
    {
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(EnrichedWireJson, JournalJsonContext.Default.EventWrapper);

        Assert.NotNull(wrapper);
        Assert.Equal("server.started", wrapper!.EventType);
        Assert.Equal("discord:tester", wrapper.Actor);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 14, 15, 39, 58, TimeSpan.Zero),
            wrapper.Timestamp);
        Assert.Equal("hotrod", wrapper.Hostname);
        // KgsmVersion binds via [JsonPropertyName("KGSMVersion")] despite the casing.
        Assert.Equal("3.0.0", wrapper.KgsmVersion);
    }

    [Fact]
    public void EventWrapper_MissingEnvelopeMetadata_IsNull()
    {
        // A pre-enrichment / minimal payload: the new envelope fields are honestly
        // absent (null), never a fabricated default.
        const string minimalWire =
            """{"EventType":"server.started","Data":{"InstanceName":"x"}}""";

        EventWrapper? wrapper =
            JsonSerializer.Deserialize(minimalWire, JournalJsonContext.Default.EventWrapper);

        Assert.NotNull(wrapper);
        Assert.Null(wrapper!.Actor);
        Assert.Null(wrapper.Timestamp);
        Assert.Null(wrapper.Hostname);
        Assert.Null(wrapper.KgsmVersion);
    }

    [Fact]
    public void EveryEventDataType_IsRegisteredInJsonContext()
    {
        // The reflection-free deserialize path throws on an unregistered type, so
        // a mapping/type added without a matching [JsonSerializable] would only
        // surface at runtime. This auto-discovers every concrete event data type
        // and asserts each resolves through the source-gen context. It walks from
        // KgsmEventDataBase, the subject-neutral root, so a non-instance event
        // (blueprint-scoped, and whatever subject comes next) is covered too — an
        // instance-only walk would leave exactly those unguarded.
        static bool IsRegistered(Type t)
        {
            try { return JournalJsonContext.Default.GetTypeInfo(t) is not null; }
            catch { return false; }
        }

        var unregistered = typeof(KgsmEventDataBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KgsmEventDataBase)) && !t.IsAbstract && t != typeof(UnrecognizedEventData))
            .Where(t => !IsRegistered(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unregistered.Count == 0,
            "Event types missing [JsonSerializable] in JournalJsonContext: "
            + string.Join(", ", unregistered));
    }

    // ---- placement: which disk an instance's files are on ------------------------------------

    // Both captured verbatim from the host journal, written by the engine's own emitter.
    private const string InstalledWireJson = """
        {"V":1,"Id":"01a02c76-c506-785f-a11d-5ecf54837ea7","EventType":"server.installed","Data":{"InstanceName":"fac-move","Blueprint":"factorio","Library":"scratch-a"},"Timestamp":"2026-08-23T02:32:56.328Z","Actor":"heisen","Origin":null,"Hostname":"hotrod","ProducerVersion":"3.17.0-rc10"}
        """;

    private const string MovedWireJson = """
        {"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a6183e","EventType":"server.moved","Data":{"InstanceName":"fac-move","FromLibrary":"scratch-a","ToLibrary":"scratch-b"},"Timestamp":"2026-08-23T02:48:21.990Z","Actor":"heisen","Origin":null,"Hostname":"hotrod","ProducerVersion":"3.17.0-rc10"}
        """;

    [Fact]
    public void InstalledEvent_NamesTheLibraryTheInstallLandedIn()
    {
        (string eventType, EventDataBase? data) = Deserialize(
            InstalledWireJson, typeof(InstanceInstalledData));

        Assert.Equal("server.installed", eventType);
        var installed = Assert.IsType<InstanceInstalledData>(data);
        Assert.Equal("fac-move", installed.InstanceName);
        Assert.Equal("factorio", installed.Blueprint);
        Assert.Equal("scratch-a", installed.Library);
    }

    [Fact]
    public void MovedEvent_NamesBothLibraries()
    {
        // Both, not just the destination: a reader that learns only where the files went cannot
        // tell which disk just got its space back, and that is the question a drain is asked.
        (string eventType, EventDataBase? data) = Deserialize(
            MovedWireJson, typeof(InstanceMovedData));

        Assert.Equal("server.moved", eventType);
        var moved = Assert.IsType<InstanceMovedData>(data);
        Assert.Equal("fac-move", moved.InstanceName);
        Assert.Equal("scratch-a", moved.FromLibrary);
        Assert.Equal("scratch-b", moved.ToLibrary);
    }

    [Fact]
    public void TransferEvents_ReadIntoTheirDataTypes()
    {
        (string exportedType, EventDataBase? exported) = Deserialize(
            """{"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61850","EventType":"server.exported","Data":{"InstanceName":"factorio-7f3a"},"Timestamp":"2026-10-29T10:00:00.000Z","Actor":"u1","Origin":"api","Hostname":"hotrod","ProducerVersion":"3.18.0-rc34"}""",
            typeof(InstanceExportedData));
        (string importedType, EventDataBase? imported) = Deserialize(
            """{"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61851","EventType":"server.imported","Data":{"InstanceName":"factorio-91ab","Blueprint":"factorio","Library":"default"},"Timestamp":"2026-10-29T10:00:00.000Z","Actor":"u1","Origin":"api","Hostname":"hotbox","ProducerVersion":"3.18.0-rc34"}""",
            typeof(InstanceImportedData));

        Assert.Equal("server.exported", exportedType);
        Assert.Equal("factorio-7f3a", Assert.IsType<InstanceExportedData>(exported).InstanceName);
        Assert.Equal("server.imported", importedType);
        var arrived = Assert.IsType<InstanceImportedData>(imported);
        Assert.Equal("factorio-91ab", arrived.InstanceName);
        Assert.Equal("factorio", arrived.Blueprint);
        Assert.Equal("default", arrived.Library);
    }

    [Fact]
    public void TransferEvents_ReadTheNodesEachHalf()
    {
        (_, KgsmEventDataBase? started) = DeserializeAny(
            """{"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61860","EventType":"server.transfer.started","Data":{"TransferId":"t-1","SourceMember":"walter","SourceInstance":"factorio-7f3a"},"Timestamp":"2026-10-29T10:00:00.000Z","Actor":"u1","Origin":"api","Hostname":"hotbox","ProducerVersion":"1.1.0"}""",
            typeof(TransferStartedEventData));
        (_, EventDataBase? completed) = Deserialize(
            """{"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61861","EventType":"server.transfer.completed","Data":{"TransferId":"t-1","InstanceName":"factorio-7f3a","TargetMember":"hotbox","TargetInstance":"factorio-91ab"},"Timestamp":"2026-10-29T10:00:00.000Z","Actor":"u1","Origin":"api","Hostname":"hotrod","ProducerVersion":"1.1.0"}""",
            typeof(TransferCompletedEventData));
        (_, EventDataBase? failedOnTarget) = Deserialize(
            """{"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61862","EventType":"server.transfer.failed","Data":{"TransferId":"t-1","SourceInstance":"factorio-7f3a","Reason":"no_room","Phase":"begin"},"Timestamp":"2026-10-29T10:00:00.000Z","Actor":"u1","Origin":"api","Hostname":"hotbox","ProducerVersion":"1.1.0"}""",
            typeof(TransferFailedEventData));

        var arriving = Assert.IsType<TransferStartedEventData>(started);
        Assert.Equal("t-1", arriving.TransferId);
        Assert.Equal("walter", arriving.SourceMember);
        Assert.Equal("factorio-7f3a", arriving.SourceInstance);
        var left = Assert.IsType<TransferCompletedEventData>(completed);
        Assert.Equal("factorio-7f3a", left.InstanceName);
        Assert.Equal("hotbox", left.TargetMember);
        Assert.Equal("factorio-91ab", left.TargetInstance);
        var failed = Assert.IsType<TransferFailedEventData>(failedOnTarget);
        Assert.Equal(string.Empty, failed.InstanceName);
        Assert.Equal("factorio-7f3a", failed.SourceInstance);
        Assert.Equal("no_room", failed.Reason);
        Assert.Equal("begin", failed.Phase);
    }

    [Theory]
    [InlineData("server.transfer.started")]
    [InlineData("server.transfer.completed")]
    [InlineData("server.transfer.failed")]
    public void TransferEvents_AreFacts(string type)
    {
        // An audit view drops phases; each half of a transfer is a node's record of it.
        Assert.Equal(EventWeight.Fact, KgsmEventCatalog.Describe(type).Weight);
    }

    [Fact]
    public void DnsNameEvents_ReadTheAnchorsRecords()
    {
        (_, KgsmEventDataBase? published) = DeserializeAny(
            """{"V":2,"Id":"01a16a05-ef80-74c0-8bee-b363d11ca3c6","EventType":"dns.name.published","Data":{"Name":"corekeeper.play.thekrystalship.com","Kind":"game","Member":"hotrod","Key":"methlab","Type":"CNAME","Content":"walter.nodes.thekrystalship.com","From":null},"Timestamp":"2026-10-23T18:28:56.320Z","Actor":"system:dns","Origin":"system","Hostname":"hotrod","ProducerVersion":"0.18.0","Severity":"info","Outcome":"success","Summary":"published the game name"}""",
            typeof(DnsNamePublishedData));
        (_, KgsmEventDataBase? withdrawn) = DeserializeAny(
            """{"V":2,"Id":"01a16a05-ef81-741f-86c8-0d8c4e8c1020","EventType":"dns.name.withdrawn","Data":{"Name":"auth.anchors.thekrystalship.com","Kind":"capability","Member":"hotrod-auth","Key":"auth","Reason":null},"Timestamp":"2026-10-23T18:28:56.321Z","Actor":"system:dns","Origin":"system","Hostname":"hotrod","ProducerVersion":"0.18.0","Severity":"warn","Outcome":"neutral","Summary":"withdrew the capability name"}""",
            typeof(DnsNameWithdrawnData));

        var p = Assert.IsType<DnsNamePublishedData>(published);
        Assert.Equal("corekeeper.play.thekrystalship.com", p.Name);
        Assert.Equal("game", p.Kind);
        Assert.Equal("hotrod", p.Member);
        Assert.Equal("methlab", p.Key);
        Assert.Equal("CNAME", p.Type);
        Assert.Equal("walter.nodes.thekrystalship.com", p.Content);
        Assert.Null(p.From);
        var w = Assert.IsType<DnsNameWithdrawnData>(withdrawn);
        Assert.Equal("auth.anchors.thekrystalship.com", w.Name);
        Assert.Equal("capability", w.Kind);
        Assert.Null(w.Reason);

        foreach (string type in new[] { "dns.name.published", "dns.name.withdrawn" })
        {
            EventDescriptor d = KgsmEventCatalog.Describe(type);
            Assert.True(d.Known);
            Assert.Equal(EventSubject.Name, d.Subject);
            Assert.Equal(EventWeight.Fact, d.Weight);
        }
        Assert.Equal(EventOutcome.Success, KgsmEventCatalog.Describe("dns.name.published").Outcome);
        Assert.Equal(EventOutcome.Neutral, KgsmEventCatalog.Describe("dns.name.withdrawn").Outcome);
    }

    [Fact]
    public void DnsNameMoved_ReadsWhereTheRecordPointedBefore()
    {
        (_, KgsmEventDataBase? data) = DeserializeAny(
            """{"V":2,"Id":"01a189f7-749a-7465-a1b9-7760be68539a","EventType":"dns.name.moved","Data":{"Name":"factorio.play.thekrystalship.com","Kind":"game","Member":"jessie","Key":"factorio","Type":"CNAME","Content":"jessie.nodes.thekrystalship.com","From":"walter.nodes.thekrystalship.com"},"Timestamp":"2026-10-29T23:20:58.266Z","Actor":"system:dns","Origin":"system","Hostname":"hotrod","ProducerVersion":"0.19.0","Severity":"info","Outcome":"success","Summary":"moved the game name"}""",
            typeof(DnsNameMovedData));

        var moved = Assert.IsType<DnsNameMovedData>(data);
        Assert.Equal("factorio.play.thekrystalship.com", moved.Name);
        Assert.Equal("jessie", moved.Member);
        Assert.Equal("jessie.nodes.thekrystalship.com", moved.Content);
        Assert.Equal("walter.nodes.thekrystalship.com", moved.From);

        EventDescriptor d = KgsmEventCatalog.Describe("dns.name.moved");
        Assert.True(d.Known);
        Assert.Equal(EventSubject.Name, d.Subject);
        Assert.Equal(EventWeight.Fact, d.Weight);
        Assert.Equal(EventOutcome.Success, d.Outcome);
    }

    [Fact]
    public void LibraryOutcomeEvents_ReadTheRefusalAndTheRename()
    {
        (_, KgsmEventDataBase? failed) = DeserializeAny(
            """{"V":2,"Id":"01a145bd-545b-7ce5-8863-30a392c9d223","EventType":"library.failed","Data":{"LibraryName":"default","Verb":"remove","Path":null,"NewName":null,"Error":"[ERROR] libraries.sh:479 Library 'default' holds instances:\n[ERROR] libraries.sh:483   minecraft","ExitCode":54},"Timestamp":"2026-10-16T17:23:18.235Z","Actor":"local:claude","Origin":"ui","Hostname":"hotrod","ProducerVersion":"0.220.0"}""",
            typeof(LibraryOutcomeEventData));
        (_, KgsmEventDataBase? renamed) = DeserializeAny(
            """{"V":2,"Id":"01a145bd-545b-7ce5-8863-30a392c9d224","EventType":"library.renamed","Data":{"LibraryName":"default","Verb":"rename","Path":null,"NewName":"games","Error":null,"ExitCode":null},"Timestamp":"2026-10-16T17:23:18.235Z","Actor":"local:claude","Origin":"ui","Hostname":"hotrod","ProducerVersion":"0.220.0"}""",
            typeof(LibraryOutcomeEventData));

        var f = Assert.IsType<LibraryOutcomeEventData>(failed);
        Assert.Equal("default", f.LibraryName);
        Assert.Equal("remove", f.Verb);
        Assert.Null(f.NewName);
        Assert.Contains("holds instances", f.Error);
        Assert.Equal(54, f.ExitCode);
        var r = Assert.IsType<LibraryOutcomeEventData>(renamed);
        Assert.Equal("games", r.NewName);
        Assert.Null(r.Error);
        Assert.Null(r.ExitCode);

        foreach (string type in new[] { "library.renamed", "library.failed" })
        {
            EventDescriptor d = KgsmEventCatalog.Describe(type);
            Assert.True(d.Known);
            Assert.Equal(EventSubject.Library, d.Subject);
            Assert.Equal(EventWeight.Fact, d.Weight);
        }
        Assert.Equal(EventOutcome.Failure, KgsmEventCatalog.Describe("library.failed").Outcome);
        Assert.Equal(EventOutcome.Neutral, KgsmEventCatalog.Describe("library.renamed").Outcome);
    }

    [Fact]
    public void MovedEvent_IsClassifiedAsAnInstanceFact()
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe("server.moved");

        Assert.True(descriptor.Known);
        Assert.Equal(typeof(InstanceMovedData), descriptor.PayloadType);
        Assert.Equal(EventSubject.Instance, descriptor.Subject);
        Assert.Equal(EventWeight.Fact, descriptor.Weight);
        Assert.Contains(descriptor.Fields, f => f.Name == "FromLibrary");
        Assert.Contains(descriptor.Fields, f => f.Name == "ToLibrary");
    }

    private const string UninstalledWireJson = """
        {"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61840","EventType":"server.uninstalled","Data":{"InstanceName":"factorio-again","InstallNonce":"9f3c0d2e6a1b4c77"},"Timestamp":"2026-09-26T02:48:21.990Z","Actor":null,"Origin":null,"Hostname":"hotrod","ProducerVersion":"3.18.0-rc28"}
        """;

    private const string OfflineUninstalledWireJson = """
        {"V":1,"Id":"01a02c84-e4e4-7b57-b8ce-3483f2a61841","EventType":"server.uninstalled","Data":{"InstanceName":"away","InstallNonce":null},"Timestamp":"2026-09-26T02:48:21.990Z","Actor":null,"Origin":null,"Hostname":"hotrod","ProducerVersion":"3.18.0-rc28"}
        """;

    [Fact]
    public void UninstalledEvent_NamesTheInstallThatWent()
    {
        (_, EventDataBase? data) = Deserialize(UninstalledWireJson, typeof(InstanceUninstalledData));

        var uninstalled = Assert.IsType<InstanceUninstalledData>(data);
        Assert.Equal("factorio-again", uninstalled.InstanceName);
        Assert.Equal("9f3c0d2e6a1b4c77", uninstalled.InstallNonce);
        Assert.Contains(KgsmEventCatalog.Describe("server.uninstalled").Fields, f => f.Name == "InstallNonce");
    }

    [Fact]
    public void UninstalledEvent_FromAnOfflineLibraryHasNoNonce()
    {
        (_, EventDataBase? data) = Deserialize(OfflineUninstalledWireJson, typeof(InstanceUninstalledData));

        Assert.Null(Assert.IsType<InstanceUninstalledData>(data).InstallNonce);
    }

    // The name→type dispatch, read off the catalog that holds it. EventService deserializes into
    // EventDescriptor.PayloadType, so this IS the table the runtime uses — not a copy of it.
    private static Dictionary<string, Type> GetEventTypeMapping() =>
        KgsmEventCatalog.All
            .Where(d => d.PayloadType is not null)
            .ToDictionary(d => d.Type, d => d.PayloadType!, StringComparer.Ordinal);

    [Fact]
    public void EveryEventDataType_HasAMappingEntry()
    {
        // Guards the (c)-direction the original incident was in: a type + its
        // [JsonSerializable] can exist while nothing in the catalog names it, in
        // which case the event is dropped at runtime as "Unknown event type" and
        // every other test still passes. This catches the omission with no external
        // dependency.
        var mapped = new HashSet<Type>(GetEventTypeMapping().Values);
        var missing = typeof(KgsmEventDataBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KgsmEventDataBase)) && !t.IsAbstract && t != typeof(UnrecognizedEventData))
            .Where(t => !mapped.Contains(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "Event types no KgsmEventCatalog descriptor names (would be dropped "
            + "as 'Unknown event type'): " + string.Join(", ", missing));
    }

    [Fact]
    public void BashEventRegistry_IsSubsetOf_CSharpMapping()
    {
        // The true bash↔C# conformance: every event kgsm can emit (its EVENT_CONFIGS
        // registry) must have a C# mapping entry — catches both a missing entry and a
        // key typo. Reads the sibling kgsm repo when colocated (the tks workspace).
        // xUnit v2 has no dynamic skip, so this no-ops in a standalone kgsm-lib
        // checkout; the always-on EveryEventDataType_HasAMappingEntry covers the same
        // missing-entry incident class without the sibling.
        string? handler = FindKgsmEventsHandler();
        if (handler is null) return;

        HashSet<string> bashEvents = ParseRegisteredBashEvents(handler!);
        Assert.NotEmpty(bashEvents);

        HashSet<string> mappingKeys = GetEventTypeMapping().Keys.ToHashSet();
        var missing = bashEvents.Where(e => !mappingKeys.Contains(e)).OrderBy(e => e).ToList();

        Assert.True(missing.Count == 0,
            "kgsm EVENT_CONFIGS events with no KgsmEventCatalog descriptor "
            + "(would be dropped as 'Unknown event type'): " + string.Join(", ", missing));
    }

    private static string? FindKgsmEventsHandler()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(
                dir.FullName, "kgsm", "commands", "handlers", "events.sh");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Parses the kgsm event registry: resolves the EVENT_* constants used as EVENT_CONFIGS keys to
    // the names they hold, which is what the catalog on this side is keyed by.
    private static HashSet<string> ParseRegisteredBashEvents(string handlerPath)
    {
        string src = File.ReadAllText(handlerPath);

        var constToValue = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(src,
            @"(EVENT_[A-Z_]+)=""([a-z._]+)"""))
        {
            constToValue[m.Groups[1].Value] = m.Groups[2].Value;
        }

        var registered = new HashSet<string>();
        foreach (System.Text.RegularExpressions.Match m in
            Regex.Matches(src, @"\[""\$(EVENT_[A-Z_]+)""\]"))
        {
            if (constToValue.TryGetValue(m.Groups[1].Value, out string? wireName))
                registered.Add(wireName);
        }
        return registered;
    }

    // ---- blueprint events: the first subject that is not an instance -------------------------------

    // All three captured verbatim from a live `kgsm events emit` through the socket transport.
    private const string BlueprintUpdatedWireJson = """
        {"EventType":"blueprint.updated","Data":{"BlueprintName":"terraria","Tier":"user","OverridesSystem":true,"Runtime":"native"},"Timestamp":"2026-07-27T18:46:50Z","Actor":"discord:987654321","Origin":"ui","Hostname":"hotrod","KGSMVersion":"3.1.2-rc9"}
        """;

    private const string BlueprintRemovedWireJson = """
        {"EventType":"blueprint.removed","Data":{"BlueprintName":"teamfortress2","Tier":"user","RevertedToSystem":false},"Timestamp":"2026-07-27T18:46:51Z","Actor":"user:heisen","Origin":"api","Hostname":"hotrod","KGSMVersion":"3.1.2-rc9"}
        """;

    // Emitted with no runtime argument and no provenance env vars: the engine renders the unknown
    // runtime and the undeclared origin as JSON null, and falls back to the invoking OS user for the actor.
    private const string BlueprintCreatedWireJson = """
        {"EventType":"blueprint.created","Data":{"BlueprintName":"odd","Tier":"user","OverridesSystem":false,"Runtime":null},"Timestamp":"2026-07-27T18:46:51Z","Actor":"heisen","Origin":null,"Hostname":"hotrod","KGSMVersion":"3.1.2-rc9"}
        """;

    [Fact]
    public void BlueprintUpdatedEvent_DeserializesWithABlueprintNameNotAnInstanceName()
    {
        (string eventType, KgsmEventDataBase? data) =
            DeserializeAny(BlueprintUpdatedWireJson, typeof(BlueprintUpdatedData));

        Assert.Equal("blueprint.updated", eventType);
        var updated = Assert.IsType<BlueprintUpdatedData>(data);
        Assert.Equal("terraria", updated.BlueprintName);
        Assert.Equal(BlueprintTier.User, updated.Tier);
        Assert.True(updated.OverridesSystem);
        Assert.Equal("native", updated.Runtime);
        // The subject is a blueprint, so there is deliberately no InstanceName to carry — this type
        // sits beside the instance-scoped hierarchy rather than inside it.
        Assert.False(data is EventDataBase);
    }

    [Fact]
    public void BlueprintRemovedEvent_DeserializesRevertedToSystemAsARealBoolean()
    {
        (string eventType, KgsmEventDataBase? data) =
            DeserializeAny(BlueprintRemovedWireJson, typeof(BlueprintRemovedData));

        Assert.Equal("blueprint.removed", eventType);
        var removed = Assert.IsType<BlueprintRemovedData>(data);
        Assert.Equal("teamfortress2", removed.BlueprintName);
        Assert.False(removed.RevertedToSystem); // nothing was restored — the blueprint is gone
    }

    [Fact]
    public void BlueprintCreatedEvent_DeserializesANullRuntimeAsUnknown()
    {
        (string eventType, KgsmEventDataBase? data) =
            DeserializeAny(BlueprintCreatedWireJson, typeof(BlueprintCreatedData));

        Assert.Equal("blueprint.created", eventType);
        var created = Assert.IsType<BlueprintCreatedData>(data);
        Assert.Equal("odd", created.BlueprintName);
        Assert.False(created.OverridesSystem);
        Assert.Null(created.Runtime); // unknown, never defaulted to "native"
    }

    [Fact]
    public void BlueprintEvent_CarriesEnvelopeProvenance()
    {
        EventWrapper? wrapper =
            JsonSerializer.Deserialize(BlueprintCreatedWireJson, JournalJsonContext.Default.EventWrapper);

        Assert.NotNull(wrapper);
        Assert.Equal("heisen", wrapper!.Actor);   // the engine's OS-user fallback
        Assert.Null(wrapper.Origin);              // no surface declared — never fabricated
        Assert.Equal("3.1.2-rc9", wrapper.KgsmVersion);
    }
}
