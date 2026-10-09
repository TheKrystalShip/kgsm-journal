using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Extensions;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for the rules every producer follows: where it writes, what it calls itself, which version
/// it stamps, and how a line gets recorded.
/// </summary>
/// <remarks>
/// These are the decisions each producer used to make for itself, and the drift they produced is what
/// this suite exists to keep out. The theme throughout is that a misconfigured producer must be
/// <em>loud</em>: a journal written where no reader scans is not reported as broken — it is not found,
/// and a producer with no journal has honestly recorded nothing, so the two are indistinguishable
/// unless something says so at the moment of writing.
/// </remarks>
public sealed class JournalConformanceTests : IDisposable
{
    private readonly string _root;

    public JournalConformanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kgsm-conformance-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    // ── Layout: the writer's path and the reader's conclusion are one rule ───────────────

    [Theory]
    [InlineData("kgsm")]
    [InlineData("kgsm-api")]
    [InlineData("kgsm-watchdog")]
    [InlineData("kgsm-firewall")]
    [InlineData("tks-auth")]
    public void ProducerOf_InvertsDirectoryFor(string producer)
    {
        // The whole point of having one rule: what the writer composes is what the reader derives.
        Assert.Equal(producer, JournalLayout.ProducerOf(JournalLayout.DirectoryFor(producer)));
    }

    [Fact]
    public void DirectoryFor_UsesTheStateDirectoryConvention()
    {
        Assert.Equal("/var/lib/kgsm-monitor/events", JournalLayout.DirectoryFor("kgsm-monitor"));
    }

    [Fact]
    public void ProducerOf_ToleratesATrailingSeparator()
    {
        // A configured path is as likely to carry one as not, and a rule that answered "no producer"
        // for a perfectly conventional path would warn about correct configuration.
        Assert.Equal("kgsm-api", JournalLayout.ProducerOf("/var/lib/kgsm-api/events/"));
    }

    [Fact]
    public void ProducerOf_AcceptsARelocatedStateRoot()
    {
        // Only the two segments that carry meaning are read, so a test root or a host that keeps state
        // elsewhere is still conventional.
        Assert.Equal("kgsm-bot", JournalLayout.ProducerOf("/tmp/somewhere/kgsm-bot/events"));
    }

    [Theory]
    [InlineData("/var/lib/kgsm-api/journal")]      // not the journal subdirectory
    [InlineData("/var/lib/kgsm-api")]              // the state directory itself
    [InlineData("/var/lib/postgres/events")]       // outside this ecosystem, so outside the scan
    [InlineData("/var/lib/tks/events")]            // the organization's prefix is "tks-", dash included
    [InlineData("/var/lib/KGSM-Api/events")]       // not a usable producer id
    [InlineData("")]
    [InlineData(null)]
    public void ProducerOf_RefusesWhatAReaderWouldNotFind(string? directory)
    {
        Assert.Null(JournalLayout.ProducerOf(directory));
    }

    // ── Self-check: the writer asks what a reader will conclude ─────────────────────────

    [Fact]
    public void Mismatch_IsSilentForTheConvention()
    {
        Assert.Null(Options("kgsm-monitor", JournalLayout.DirectoryFor("kgsm-monitor")).DescribeDirectoryMismatch());
    }

    [Fact]
    public void Mismatch_IsSilentForARelocatedRoot()
    {
        // Test isolation and a host with state elsewhere are legitimate; neither changes the rule.
        Assert.Null(Options("kgsm-api", Path.Combine(_root, "kgsm-api", "events")).DescribeDirectoryMismatch());
    }

    [Fact]
    public void Mismatch_NamesTheProducerAReaderWouldAttributeInstead()
    {
        // The copy-paste that is one plausible mistake away: a leaf naming its producer after its unit
        // while writing to its state directory. Every event lands under the other name.
        string? problem = Options("kgsm-widget-service", "/var/lib/kgsm-widget/events")
            .DescribeDirectoryMismatch();

        Assert.NotNull(problem);
        Assert.Contains("kgsm-widget", problem);
        Assert.Contains("kgsm-widget-service", problem);
    }

    [Fact]
    public void Mismatch_SaysWhenNoReaderWouldFindItAtAll()
    {
        string? problem = Options("kgsm-api", "/opt/kgsm-api/audit").DescribeDirectoryMismatch();

        Assert.NotNull(problem);
        Assert.Contains("no producer", problem, StringComparison.OrdinalIgnoreCase);
    }

    // ── Version: one build identity, never a fabricated one ─────────────────────────────

    [Fact]
    public void Version_PrefersTheInformationalVersion()
    {
        // The number that matches what a repo publishes and a consumer pins, complete with whatever
        // source revision the build stamps on — where the assembly version is a four-part form no
        // released package is ever numbered with. This pairing is the drift the ecosystem had: three
        // producers stamping the right-hand value and one the left.
        Assert.Equal("2.5.2+3d0f4386", ProducerVersion.Resolve("2.5.2+3d0f4386", "2.5.2.0"));
    }

    [Fact]
    public void Version_FallsBackToTheAssemblyVersion()
    {
        Assert.Equal("1.5.1.0", ProducerVersion.Resolve(null, "1.5.1.0"));
        Assert.Equal("1.5.1.0", ProducerVersion.Resolve("  ", "1.5.1.0"));
    }

    [Fact]
    public void Version_IsNeverFabricated()
    {
        // Absent is the honest answer and the envelope already defines it — a reader sees no claim
        // about the build. A placeholder like 0.0.0 would name a build that was never made.
        Assert.Null(ProducerVersion.Resolve(null, null));
        Assert.Null(ProducerVersion.Resolve("", "  "));
    }

    [Fact]
    public void Version_ReadsARealAssembly()
    {
        // The wiring above the rule: an SDK-built assembly always carries an informational version, so
        // this is what every producer will actually stamp.
        Assembly assembly = typeof(JournalConformanceTests).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informational));
        Assert.Equal(informational, ProducerVersion.Of(assembly));
    }

    // ── Identity: the actor is derived, not declared twice ──────────────────────────────

    [Theory]
    [InlineData("kgsm-watchdog", "system:watchdog")]
    [InlineData("kgsm-monitor", "system:monitor")]
    [InlineData("kgsm-firewall", "system:firewall")]
    [InlineData("kgsm", "system:kgsm")]
    public void SystemActor_IsDerivedFromTheProducer(string producer, string expected)
    {
        // The first two are the strings the watchdog and the monitor each held as their own constant.
        // Deriving them has to produce exactly those, or migrating a producer rewrites its history's
        // actor.
        Assert.Equal(expected, JournalProducer.SystemActorFor(producer));
    }

    // ── Presence: discoverable before it has anything to say ────────────────────────────

    [Fact]
    public void Registration_CreatesTheJournalDirectory()
    {
        // A consumer scans for producers when it starts. Left to the first event, a deployed producer
        // with nothing yet to record is invisible until it emits AND every consumer restarts.
        string directory = Path.Combine(_root, "kgsm-monitor", "events");
        Assert.False(Directory.Exists(directory));

        new ServiceCollection().AddKgsmJournal(
            "kgsm-monitor",
            typeof(JournalConformanceTests).Assembly,
            configure: o => o.Directory = directory);

        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void Registration_RelocatesEveryJournalUnderOneStateRoot()
    {
        // Deriving the path from the producer id means a component run by hand writes exactly where
        // the deployed one does — into this host's real audit record. There has to be one way to move
        // the whole layout aside, and it moves the root without bending the rule: same producer name,
        // same events subdirectory, somewhere else.
        new ServiceCollection().AddKgsmJournal(
            "kgsm-monitor", typeof(JournalConformanceTests).Assembly, stateRoot: _root);

        Assert.True(Directory.Exists(Path.Combine(_root, "kgsm-monitor", "events")));
    }

    [Fact]
    public void Registration_ReadsTheStateRootFromTheEnvironment()
    {
        string previous = Environment.GetEnvironmentVariable(
            JournalServiceCollectionExtensions.StateRootVariable) ?? string.Empty;

        try
        {
            Environment.SetEnvironmentVariable(
                JournalServiceCollectionExtensions.StateRootVariable, _root);

            new ServiceCollection().AddKgsmJournal(
                "kgsm-bot", typeof(JournalConformanceTests).Assembly);

            Assert.True(Directory.Exists(Path.Combine(_root, "kgsm-bot", "events")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                JournalServiceCollectionExtensions.StateRootVariable,
                previous.Length == 0 ? null : previous);
        }
    }

    [Fact]
    public void Registration_ResolvesOneWriterForTheProducer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddKgsmJournal(
            "kgsm-bot", typeof(JournalConformanceTests).Assembly,
            configure: o => o.Directory = Path.Combine(_root, "kgsm-bot", "events"));

        using ServiceProvider provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IEventJournalWriter>();

        Assert.Equal("kgsm-bot", writer.Producer);
        Assert.Same(writer, provider.GetRequiredService<IEventJournalWriter>());
    }

    [Fact]
    public void Writer_CreatesItsDirectoryWhenConstructedDirectly()
    {
        string directory = Path.Combine(_root, "kgsm-scheduler", "events");

        _ = new EventJournalWriter(
            Options("kgsm-scheduler", directory), new Mock<ILogger<EventJournalWriter>>().Object);

        Assert.True(Directory.Exists(directory));
    }

    // ── Resolution: which reader a consumer gets, in either call order ──────────────────

    [Theory]
    [InlineData(true)]   // AddKgsmServices first, then federation — the order that always worked
    [InlineData(false)]  // federation first — the order that silently did nothing
    public void Federation_WinsWhicheverOrderItWasRegisteredIn(bool servicesFirst)
    {
        // The regression this exists for has no symptom. Two valid AddSingleton registrations of one
        // interface differ only in call order, so a consumer that federated too early kept reading its
        // single journal SUCCESSFULLY — healthy journal, quiet host, nothing to catch. Asserting that
        // resolution succeeded proves nothing; the assertion has to name the type.
        using ServiceProvider provider = BuildProvider(servicesFirst);

        Assert.IsType<FederatedEventSource>(provider.GetRequiredService<IEventSource>());
        Assert.IsType<FederatedEventJournalHistory>(provider.GetRequiredService<IEventJournalHistory>());
    }

    [Fact]
    public void WithoutFederation_TheSingleJournalReaderIsResolved()
    {
        // The other half of the rule: a consumer that never federates is unaffected by the resolution
        // seam existing at all.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddKgsmServices(KgsmOptionsForTests());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<EventJournalReader>(provider.GetRequiredService<IEventSource>());
        Assert.IsType<EventJournalHistory>(provider.GetRequiredService<IEventJournalHistory>());
    }

    [Fact]
    public void AConsumerCanStillSupplyItsOwnSource()
    {
        // Order-independence must not become "the library decides and you cannot". An explicit
        // registration after the fact still wins, the same way it does for IEventCursorStore.
        var own = new Mock<IEventSource>().Object;

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddKgsmServices(KgsmOptionsForTests());
        services.AddKgsmJournalFederation(engineJournalDirectory: _root, stateRoot: _root);
        services.AddSingleton(own);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<IEventSource>());
    }

    [Fact]
    public void Discovery_ScansOnceHoweverManyReadersAskIt()
    {
        // The history reader and the live tail are built from this, and they have to see the same set
        // of producers: a journal appearing between two scans would leave one half of a consumer
        // permanently blind to a producer the other half reports on.
        Directory.CreateDirectory(Path.Combine(_root, "kgsm-watchdog", "events"));

        var discovery = new JournalDiscovery(
            Path.Combine(_root, "kgsm", "events"), _root, NullLogger<JournalDiscovery>.Instance);

        IReadOnlyList<JournalSource> first = discovery.Discover();

        // A producer that starts writing after the first scan is deliberately NOT picked up: the set is
        // fixed for the life of the process, so both readers keep agreeing.
        Directory.CreateDirectory(Path.Combine(_root, "kgsm-monitor", "events"));

        Assert.Same(first, discovery.Discover());
        Assert.DoesNotContain(discovery.Discover(), s => s.Producer == "kgsm-monitor");
    }

    [Fact]
    public void Discovery_FindsTheOrganizationsServicesBesideTheEcosystems()
    {
        // tks-auth records what happened to the cluster's accounts; a scan blind to it shows an audit
        // with every sign-in missing and no sign that anything is.
        Directory.CreateDirectory(Path.Combine(_root, "kgsm-api", "events"));
        Directory.CreateDirectory(Path.Combine(_root, "tks-auth", "events"));
        Directory.CreateDirectory(Path.Combine(_root, "postgres", "events"));

        var discovery = new JournalDiscovery(
            Path.Combine(_root, "kgsm", "events"), _root, NullLogger<JournalDiscovery>.Instance);

        Assert.Equal(
            ["kgsm", "kgsm-api", "tks-auth"],
            discovery.Discover().Select(s => s.Producer).ToArray());
    }

    // ── Reachability: a journal no other account can enter ──────────────────────────────

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute)]                                  // 0750
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute)]                                  // 0755
    public void Reachability_IsSilentWhenTheGroupCanEnter(UnixFileMode mode)
    {
        // The ecosystem's answer to cross-account reads is a shared group, so a state directory that
        // grants the group execute is correct and must produce no noise — these are the two modes
        // every unit on this host actually declares.
        string dir = Segments("kgsm-api");
        File.SetUnixFileMode(Path.GetDirectoryName(dir)!, mode);

        Assert.Null(JournalAccess.DescribeUnreachable(dir));
    }

    [Fact]
    public void Reachability_ReportsAStateDirectoryTheGroupCannotEnter()
    {
        // The failure this exists for is silence: a reader that cannot traverse in gets no permission
        // error, it gets Directory.Exists == false — which discovery reads as a producer that has
        // recorded nothing. Nothing on the host distinguishes the two.
        string dir = Segments("kgsm-api");
        File.SetUnixFileMode(
            Path.GetDirectoryName(dir)!,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);          // 0700

        string? problem = JournalAccess.DescribeUnreachable(dir);

        Assert.NotNull(problem);
        Assert.Contains("recorded nothing", problem);
    }

    [Fact]
    public void Reachability_SaysNothingAboutADirectoryItCannotSee()
    {
        // A mode this process cannot read is not evidence of a bad one, and warning about a correctly
        // configured host is its own kind of wrong.
        Assert.Null(JournalAccess.DescribeUnreachable(Path.Combine(_root, "absent", "events")));
        Assert.Null(JournalAccess.DescribeUnreachable(null));
    }

    // ── The line's own id (§2·m) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Writer_GivesEveryLineAUuidV7()
    {
        string dir = Segments("kgsm-monitor");
        IEventJournalWriter writer = new EventJournalWriter(
            Options("kgsm-monitor", dir, clock: () => At("2026-08-16")),
            new Mock<ILogger<EventJournalWriter>>().Object);

        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        string line = File.ReadAllLines(Path.Combine(dir, "2026-08-16.ndjson"))[^1];
        string id = System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("Id").GetString()!;

        // The version nibble and the variant bits, asserted rather than assumed: a v4 would satisfy
        // "is a guid" and lose the ordering the whole choice was made for.
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", id);
    }

    [Fact]
    public async Task Writer_GivesTwoIdenticalEventsDifferentIds()
    {
        // The reason the id is minted and never derived from content. Two identical events in the
        // same second are two events; a digest over the line would fold them into one, which is the
        // defect the engine's own index has.
        string dir = Segments("kgsm-monitor");
        IEventJournalWriter writer = new EventJournalWriter(
            Options("kgsm-monitor", dir, clock: () => At("2026-08-16")),
            new Mock<ILogger<EventJournalWriter>>().Object);

        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());
        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        string[] lines = File.ReadAllLines(Path.Combine(dir, "2026-08-16.ndjson"));
        string[] ids = [.. lines.Select(l =>
            System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("Id").GetString()!)];

        Assert.Equal(2, ids.Length);
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public async Task Writer_ProducesALineThatStillConforms()
    {
        // The ordering constraint behind §2·m, closed end to end: the checker has to already know the
        // field, or the first producer to emit an id reports every line as having invented one.
        string dir = Segments("kgsm-monitor");
        IEventJournalWriter writer = new EventJournalWriter(
            Options("kgsm-monitor", dir, clock: () => At("2026-08-16")),
            new Mock<ILogger<EventJournalWriter>>().Object);

        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload(), actor: "system:monitor", origin: "system");

        string line = File.ReadAllLines(Path.Combine(dir, "2026-08-16.ndjson"))[^1];

        Assert.Empty(TheKrystalShip.KGSM.Conformance.JournalConformance.CheckLine("kgsm-monitor", "2026-08-16.ndjson", 1, line));
    }

    // ── Retention ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Retention_LeavesEveryKeptSegmentByteIdentical()
    {
        // §2·l as a test. Every stored position on this host — the reactor's ledger, the audit ids the
        // API derives and paginates on — is a byte offset into a named segment, so a pruner that
        // rewrote a file rather than unlinking it would silently misplace every event after the cut.
        // Asserted on the bytes rather than on "the file is still there", because copytruncate and
        // dropping the first N lines both leave the file there.
        string dir = Segments("kgsm-api", "2026-01-01", "2026-08-16");
        string kept = Path.Combine(dir, "2026-08-16.ndjson");

        // Several lines, not the helper's single one: the failure §2·l names is dropping the first N
        // lines of a segment that is otherwise kept, and a one-line fixture cannot tell that apart
        // from removing the file.
        File.WriteAllText(kept, """{"V":1,"EventType":"a"}""" + "\n"
            + """{"V":1,"EventType":"b"}""" + "\n"
            + """{"V":1,"EventType":"c"}""" + "\n");
        byte[] before = File.ReadAllBytes(kept);

        JournalRetention.Prune(dir, 90, At("2026-08-16"), NullLogger.Instance);

        Assert.Equal(before, File.ReadAllBytes(kept));
    }

    [Fact]
    public void Retention_RemovesOnlySegmentsPastTheWindow()
    {
        string dir = Segments("kgsm-api", "2026-01-01", "2026-05-17", "2026-05-18", "2026-08-16");

        // 90 days before 2026-08-16 is 2026-05-18, and a segment dated exactly on the boundary is
        // kept: the window is "90 days of history", and rounding it inward returns one day less than
        // the number an operator configured.
        int removed = JournalRetention.Prune(dir, 90, At("2026-08-16"), NullLogger.Instance);

        Assert.Equal(2, removed);
        Assert.Equal(
            ["2026-05-18.ndjson", "2026-08-16.ndjson"],
            Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public void Retention_LeavesAnythingItDidNotWrite()
    {
        // The directory belongs to one producer, which is a reason to be careful with it rather than a
        // licence to delete whatever is in it.
        string dir = Segments("kgsm-api", "2020-01-01");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "cursor.ndjson"), "x");

        Assert.Equal(1, JournalRetention.Prune(dir, 90, At("2026-08-16"), NullLogger.Instance));
        Assert.Equal(
            ["cursor.ndjson", "notes.txt"],
            Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).Order().ToArray());
    }

    [Fact]
    public void Retention_AgeComesFromTheNameNotTheMtime()
    {
        // A restore, a copy or a backup tool moves an mtime without any event moving. The segment's
        // name is its date whatever the filesystem thinks.
        string dir = Segments("kgsm-api", "2020-01-01");
        File.SetLastWriteTimeUtc(Path.Combine(dir, "2020-01-01.ndjson"), new DateTime(2026, 8, 16));

        Assert.Equal(1, JournalRetention.Prune(dir, 90, At("2026-08-16"), NullLogger.Instance));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Retention_KeepsEverythingWhenDisabled(int days)
    {
        string dir = Segments("kgsm-api", "2001-01-01");

        Assert.Equal(0, JournalRetention.Prune(dir, days, At("2026-08-16"), NullLogger.Instance));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public void Retention_IsNeverFatal()
    {
        // Housekeeping. A producer that cannot prune must still be able to record what it did.
        Assert.Equal(0, JournalRetention.Prune(
            Path.Combine(_root, "nothing-here"), 90, At("2026-08-16"), NullLogger.Instance));
    }

    [Fact]
    public void Writer_PrunesAtStartup()
    {
        // The only moment a socket-activated authority ever reaches: it may exist for the length of one
        // request, so a timer would never fire.
        string dir = Segments("kgsm-firewall", "2020-01-01");

        _ = new EventJournalWriter(
            Options("kgsm-firewall", dir, clock: () => At("2026-08-16")),
            new Mock<ILogger<EventJournalWriter>>().Object);

        Assert.Empty(Directory.GetFiles(dir, "*.ndjson"));
    }

    [Fact]
    public async Task Writer_PrunesWhenTheSegmentRollsOver()
    {
        // The second moment, and the one a resident daemon lives on: a segment is a day, so a rollover
        // is a daily cadence that needs no timer and no hosting stack to produce.
        string dir = Segments("kgsm-monitor");
        DateTimeOffset now = At("2026-08-16");

        IEventJournalWriter writer = new EventJournalWriter(
            Options("kgsm-monitor", dir, clock: () => now),
            new Mock<ILogger<EventJournalWriter>>().Object);

        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        // A segment that ages past the window while the process is running.
        File.WriteAllText(Path.Combine(dir, "2026-05-01.ndjson"), "{}\n");
        Assert.True(File.Exists(Path.Combine(dir, "2026-05-01.ndjson")));

        now = At("2026-08-17");
        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        Assert.False(File.Exists(Path.Combine(dir, "2026-05-01.ndjson")));
        Assert.True(File.Exists(Path.Combine(dir, "2026-08-17.ndjson")));
    }

    [Fact]
    public async Task Writer_DoesNotRescanOnEveryAppend()
    {
        // Guarded to once a day rather than once per event: the scan is cheap, and doing it on a write
        // path a supervisor calls three times per server start is still work for nothing.
        string dir = Segments("kgsm-monitor");
        IEventJournalWriter writer = new EventJournalWriter(
            Options("kgsm-monitor", dir, clock: () => At("2026-08-16")),
            new Mock<ILogger<EventJournalWriter>>().Object);

        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        // Dropped in after the first append; nothing else rolls the segment, so nothing rescans.
        File.WriteAllText(Path.Combine(dir, "2020-01-01.ndjson"), "{}\n");
        await writer.AppendAsync(EventName.Parse("thing_happened"), Payload());

        Assert.True(File.Exists(Path.Combine(dir, "2020-01-01.ndjson")));
    }

    // ── The write path ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Record_StampsTheDerivedActorAndSystemOrigin()
    {
        var recorder = new TestRecorder(Writer("kgsm-watchdog"));

        await recorder.RecordDefaultsAsync();

        JsonElement line = ReadOnlyLine("kgsm-watchdog");
        Assert.Equal("system:watchdog", line.GetProperty("Actor").GetString());
        Assert.Equal("system", line.GetProperty("Origin").GetString());
    }

    [Fact]
    public async Task Record_LetsTheCallSiteOverrideBoth()
    {
        // Carrying a caller's provenance is what a producer acting on somebody's behalf does; the
        // default is for a producer acting on its own.
        var recorder = new TestRecorder(Writer("kgsm-watchdog"));

        await recorder.RecordAsAsync("discord:someone", "ui");

        JsonElement line = ReadOnlyLine("kgsm-watchdog");
        Assert.Equal("discord:someone", line.GetProperty("Actor").GetString());
        Assert.Equal("ui", line.GetProperty("Origin").GetString());
    }

    [Fact]
    public async Task Record_OmitsAnActorAProducerCannotKnow()
    {
        // A producer whose events are driven by people overrides the default away. Attributing an
        // unknown actor to the daemon that carried the action out would state an author it has not got.
        var recorder = new TestRecorder(Writer("kgsm-api")) { SuppressDefaultActor = true };

        await recorder.RecordDefaultsAsync();

        JsonElement line = ReadOnlyLine("kgsm-api");
        Assert.False(line.TryGetProperty("Actor", out _));
    }

    [Fact]
    public async Task Record_NormalisesADashSeparatedType()
    {
        var recorder = new TestRecorder(Writer("kgsm-watchdog"));

        // Named for nothing in the vocabulary on purpose: this is about the spelling a call site may
        // use, not about which events exist. Resolving a name that has been renamed is a separate
        // thing that happens where a line is read.
        await recorder.RecordTypeAsync("thing-happened");

        Assert.Equal("thing_happened", ReadOnlyLine("kgsm-watchdog").GetProperty("EventType").GetString());
    }

    [Fact]
    public async Task Record_ReportsAFailedWriteWithoutThrowing()
    {
        // The action already happened. Refusing it because the record could not be written would trade
        // a missing line for broken behaviour — so the caller is told, and decides.
        var writer = new Mock<IEventJournalWriter>();
        writer.SetupGet(w => w.Producer).Returns("kgsm-monitor");
        writer
            .Setup(w => w.AppendAsync(
                It.IsAny<EventName>(), It.IsAny<JsonElement>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<EventSeverity?>(), It.IsAny<EventOutcome?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk gone"));

        var recorder = new TestRecorder(writer.Object);

        Assert.False(await recorder.RecordDefaultsAsync());
    }

    [Fact]
    public void Record_WritesARealNullRatherThanAnEmptyString()
    {
        // An empty string is a third state the envelope does not define: neither a value nor the
        // absence of one, and a reader checking for null does not find it.
        var recorder = new TestRecorder(Writer("kgsm-monitor"));

        recorder.RecordNullablePayload(null);

        JsonElement data = ReadOnlyLine("kgsm-monitor").GetProperty("Data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("Reason").ValueKind);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>A journal directory for <paramref name="producer"/> holding the named segments.</summary>
    private string Segments(string producer, params string[] dates)
    {
        string dir = Path.Combine(_root, producer, "events");
        Directory.CreateDirectory(dir);

        foreach (string date in dates)
            File.WriteAllText(Path.Combine(dir, date + ".ndjson"), "{}\n");

        return dir;
    }

    private static DateTimeOffset At(string date) =>
        DateTimeOffset.Parse(date + "T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static Action<System.Text.Json.Utf8JsonWriter> Payload() =>
        w => w.WriteString("Subject", "x");

    /// <summary>A container with both registrations, made in the order under test.</summary>
    private ServiceProvider BuildProvider(bool servicesFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        if (servicesFirst)
        {
            services.AddKgsmServices(KgsmOptionsForTests());
            services.AddKgsmJournalFederation(engineJournalDirectory: _root, stateRoot: _root);
        }
        else
        {
            services.AddKgsmJournalFederation(engineJournalDirectory: _root, stateRoot: _root);
            services.AddKgsmServices(KgsmOptionsForTests());
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Options pointed at this test's own root, so nothing reads the machine's journals.</summary>
    private KgsmOptions KgsmOptionsForTests() => new()
    {
        KgsmPath = Path.Combine(_root, "kgsm.sh"),
        EventJournalDirectory = Path.Combine(_root, "kgsm", "events"),
    };

    private EventJournalWriterOptions Options(
        string producer, string directory, Func<DateTimeOffset>? clock = null) => new()
    {
        Producer = producer,
        Directory = directory,
        Hostname = "testhost",
        Clock = clock,
    };

    private EventJournalWriter Writer(string producer) => new(
        Options(producer, Path.Combine(_root, producer, "events")),
        new Mock<ILogger<EventJournalWriter>>().Object);

    private JsonElement ReadOnlyLine(string producer)
    {
        string directory = Path.Combine(_root, producer, "events");
        string[] segments = Directory.GetFiles(directory, "*.ndjson");
        string line = Assert.Single(File.ReadAllLines(Assert.Single(segments)));

        return JsonDocument.Parse(line).RootElement.Clone();
    }

    /// <summary>A producer, standing in for the four real ones so the shared half can be exercised.</summary>
    private sealed class TestRecorder(IEventJournalWriter writer)
        : JournalRecorder(writer, new Mock<ILogger>().Object)
    {
        public bool SuppressDefaultActor { get; init; }

        protected override string? DefaultActor => SuppressDefaultActor ? null : base.DefaultActor;

        public Task<bool> RecordDefaultsAsync() =>
            RecordAsync(EventName.Parse("thing_happened"), w => w.WriteString("Subject", "x"));

        public Task<bool> RecordAsAsync(string actor, string origin) =>
            RecordAsync(EventName.Parse("thing_happened"), w => w.WriteString("Subject", "x"), actor, origin);

        public Task<bool> RecordTypeAsync(string eventType) =>
            RecordAsync(EventName.Parse(eventType), w => w.WriteString("Subject", "x"));

        public bool RecordNullablePayload(string? reason) =>
            Record(EventName.Parse("thing_happened"), w => WriteNullable(w, "Reason", reason));
    }
}
