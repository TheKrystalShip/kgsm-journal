using System.Globalization;
using TheKrystalShip.KGSM.Conformance;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for the check that reads what producers wrote and reports where it disagrees.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is tested twice: once against something that breaks it, and once against something that
/// looks similar and does not. A conformance check that only ever fires is a check nobody can leave
/// switched on, and the second half of each pair is what keeps it usable — the shapes that must
/// <em>not</em> be reported are drawn from what producers on a live host actually write.
/// </para>
/// <para>
/// The rules are exercised over synthetic lines, so this suite says nothing about any host. The check
/// against real journals is <see cref="HostJournalConformanceTests"/>, and it needs one.
/// </para>
/// </remarks>
public sealed class JournalConformanceCheckerTests : IDisposable
{
    private const string Producer = "kgsm-monitor";

    private readonly string _root;

    public JournalConformanceCheckerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kgsm-conformance-checker", Path.GetRandomFileName());
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

    // ── A conforming line ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_line_a_producer_writes_today_breaks_no_rule()
    {
        IReadOnlyList<ConformanceFinding> findings = Check(Line());

        Assert.Empty(findings);
    }

    [Theory]
    // Every distinct envelope shape measured across the five journals on a live host: an unclaimed
    // engine action with an explicit null actor and origin, a leaf's derived system actor, a
    // surface-driven action, and a producer that omits the optional fields entirely.
    [InlineData("""{"V":1,"EventType":"server.install.created","Data":{"instance":"a"},"Timestamp":"2026-08-16T10:04:37.799Z","Actor":null,"Origin":null,"Hostname":"hotrod","ProducerVersion":"3.16.0-rc3"}""")]
    [InlineData("""{"V":1,"EventType":"player.left","Data":{"instance":"a"},"Timestamp":"2026-08-16T11:22:47.317Z","Actor":"system:watchdog","Origin":"system","Hostname":"hotrod","ProducerVersion":"1.30.2+f0b7744e2e06"}""")]
    [InlineData("""{"V":1,"EventType":"network.ports.opened","Data":{"instance":"a"},"Timestamp":"2026-08-16T09:42:43.417Z","Actor":"discord:heisen9386","Origin":"ui","Hostname":"hotrod","ProducerVersion":"1.7.1+39bf5a539021"}""")]
    [InlineData("""{"V":1,"EventType":"auth.signed_out","Data":{},"Timestamp":"2026-08-15T22:41:51.402Z"}""")]
    public void The_shapes_a_live_host_writes_all_conform(string line)
    {
        IReadOnlyList<ConformanceFinding> findings = Check(line);

        Assert.Empty(findings);
    }

    // ── line.parses ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"V":1,""")]
    [InlineData("""[{"V":1}]""")]
    [InlineData("\"a bare string\"")]
    public void A_line_that_is_not_one_json_object_is_reported_and_checked_no_further(string line)
    {
        IReadOnlyList<ConformanceFinding> findings = Check(line);

        // One finding, not a cascade: a line that could not be parsed has no fields to complain about,
        // and reporting eight missing ones would bury the only fact that matters.
        ConformanceFinding only = Assert.Single(findings);
        Assert.Equal(ConformanceRule.LineParses, only.Rule);
    }

    // ── envelope.schema-version ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(""""{"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":"1","EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":99,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    public void A_line_that_does_not_declare_this_contract_is_reported(string line)
    {
        AssertBreaks(ConformanceRule.SchemaVersion, line);
    }

    [Fact]
    public void The_version_the_writer_stamps_is_the_version_the_check_accepts()
    {
        // Not a restatement of the constant: it is what stops the two from being bumped separately,
        // which would make the check reject every line the writer produces.
        IReadOnlyList<ConformanceFinding> findings =
            Check(Line(version: EventJournalWriter.SchemaVersion.ToString(CultureInfo.InvariantCulture)));

        Assert.Empty(findings);
    }

    // ── envelope.event-type ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(""""{"V":1,"Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":7,"Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"instance-ready","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"Instance_Ready","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"instance ready","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    public void An_event_type_no_consumer_would_match_is_reported(string line)
    {
        AssertBreaks(ConformanceRule.EventType, line);
    }

    // ── envelope.data ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(""""{"V":1,"EventType":"a_b","Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"a_b","Data":"instance","Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"a_b","Data":[],"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    [InlineData(""""{"V":1,"EventType":"a_b","Data":null,"Timestamp":"2026-08-16T10:04:37.799Z"}"""")]
    public void A_payload_a_reader_cannot_key_a_subject_off_is_reported(string line)
    {
        AssertBreaks(ConformanceRule.Data, line);
    }

    [Fact]
    public void An_event_with_nothing_to_say_still_says_it_as_an_object()
    {
        Assert.Empty(Check(Line(data: "{}")));
    }

    // ── envelope.timestamp ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-16T10:04:37Z")]              // second precision
    [InlineData("2026-08-16T10:04:37.799")]           // no zone
    [InlineData("2026-08-16T10:04:37.799+02:00")]     // an offset, not UTC
    [InlineData("2026-08-16T10:04:37.7991234Z")]      // more precision than the contract
    [InlineData("2026-08-16 10:04:37.799Z")]          // space instead of T
    [InlineData("1755338677799")]                     // epoch millis
    public void A_timestamp_that_would_not_merge_correctly_is_reported(string stamp)
    {
        AssertBreaks(ConformanceRule.Timestamp, Line(timestamp: stamp));
    }

    [Fact]
    public void The_stamp_the_writer_formats_is_the_stamp_the_check_parses()
    {
        // The round trip, over the one constant both use. A format string that formatted one way and
        // parsed another would make every line the writer produces fail its own check.
        string written = new DateTimeOffset(2026, 8, 16, 10, 4, 37, 799, TimeSpan.Zero)
            .UtcDateTime.ToString(EventJournalWriter.TimestampFormat, CultureInfo.InvariantCulture);

        Assert.Equal("2026-08-16T10:04:37.799Z", written);
        Assert.Empty(Check(Line(timestamp: written)));
    }

    // ── envelope.absent-spelling ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Actor")]
    [InlineData("Origin")]
    [InlineData("Hostname")]
    [InlineData("ProducerVersion")]
    [InlineData("Id")]
    [InlineData("OpId")]
    [InlineData("RunId")]
    public void An_empty_string_where_a_value_is_absent_is_reported(string field)
    {
        string line = $$"""
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","{{field}}":""}
        """;

        AssertBreaks(ConformanceRule.AbsentSpelling, line);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"local:someone\"")]
    public void The_two_ways_the_contract_defines_are_both_accepted(string actor)
    {
        // Absent and null are the same thing to a reader, so a producer may spell absence either way
        // and neither is drift. Only the third state — an empty string — is undefined.
        string line = $$"""
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Actor":{{actor}}}
        """;

        Assert.Empty(Check(line));
    }

    [Fact]
    public void A_field_left_out_entirely_is_not_a_finding()
    {
        Assert.Empty(Check("""{"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}"""));
    }

    // ── envelope.actor ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(":heisen")]
    [InlineData("discord:")]
    public void A_half_written_qualified_actor_is_reported(string actor)
    {
        AssertBreaks(ConformanceRule.Actor, Line(actor: actor));
    }

    [Theory]
    [InlineData("system:watchdog")]
    [InlineData("discord:Claude (agent)")]
    [InlineData("local:claude")]
    [InlineData("github:octocat")]
    public void An_actor_a_producer_on_this_host_writes_is_accepted(string actor)
    {
        // Which providers a host has is its own configuration, so an unrecognised one is not drift:
        // the reader keeps the name rather than coercing it into a provider it happens to know.
        Assert.Empty(Check(Line(actor: actor)));
    }

    [Theory]
    [InlineData("heisen")]
    [InlineData("root")]
    [InlineData("claude")]
    public void A_bare_name_is_reported_as_an_actor_naming_no_provider(string actor)
    {
        // A name with no provider in front of it is the shape an OS username takes, and the OS user
        // owns the process rather than asking for the action — so it names the wrong principal on an
        // audit record even when the string is a real login on this host.
        ConformanceFinding finding = Assert.Single(Check(Line(actor: actor)));
        Assert.Equal(ConformanceRule.Actor, finding.Rule);
    }

    // ── envelope.producer-version-shape ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.3.0.0")]
    [InlineData("0.99.1.0")]
    [InlineData("1.5.0.0")]
    public void A_four_part_assembly_version_is_reported(string version)
    {
        // The measured drift, exactly: no released package is numbered this way, so the field matches
        // no tag, no pin, and no other producer's stamp.
        AssertBreaks(ConformanceRule.ProducerVersionShape, Line(producerVersion: version));
    }

    [Theory]
    [InlineData("3.16.0-rc3")]
    [InlineData("1.30.2+f0b7744e2e06")]
    [InlineData("2.7.1")]
    [InlineData("1.2.3.4-rc1")]
    [InlineData("0.101.0+cac2fdf76dfc")]
    public void A_version_a_release_actually_carries_is_accepted(string version)
    {
        Assert.Empty(Check(Line(producerVersion: version)));
    }

    [Fact]
    public void The_version_resolver_and_the_shape_check_agree_on_what_is_wrong()
    {
        // The rule the resolver enforces and the rule the check reports are the same rule. Pinned
        // together so the check cannot start accepting the shape the resolver exists to avoid.
        string? resolved = ProducerVersion.Resolve(informationalVersion: null, assemblyVersion: "2.3.0.0");

        Assert.Equal("2.3.0.0", resolved);
        Assert.True(JournalConformance.IsFourPartAssemblyVersion(resolved));
        Assert.False(JournalConformance.IsFourPartAssemblyVersion(
            ProducerVersion.Resolve("2.3.0+abc", "2.3.0.0")));
    }

    // ── envelope.unknown-field ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_field_the_envelope_does_not_define_is_reported()
    {
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Instance":"a"}
        """;

        ConformanceFinding finding = AssertBreaks(ConformanceRule.UnknownField, line);
        Assert.Contains("Instance", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_carrying_its_own_id_conforms()
    {
        // The ordering constraint behind the line id, as a test. The checker has to know this field BEFORE
        // any producer emits it: otherwise the first producer to ship an id has every line it writes
        // reported as having invented a field, and the host conformance check goes red on a fleet that
        // is doing exactly what the contract asks.
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Id":"0198f3a2-7c41-7b3e-9f2a-1d4c8e5b6a70"}
        """;

        Assert.Empty(Check(line));
    }

    [Fact]
    public void A_line_with_no_id_still_conforms()
    {
        // Optional, and optional forever: every line written before the id existed is on disk for as
        // long as retention holds it. Absent is unknown, never a fault.
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z"}
        """;

        Assert.Empty(Check(line));
    }

    // ── envelope.event-id-shape ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0198F3A2-7C41-7B3E-9F2A-1D4C8E5B6A70", "uppercase reads as a different id")]
    [InlineData("0198f3a27c417b3e9f2a1d4c8e5b6a70", "unhyphenated reads as a different id")]
    [InlineData("{0198f3a2-7c41-7b3e-9f2a-1d4c8e5b6a70}", "braced reads as a different id")]
    [InlineData("0198f3a2-7c41-4b3e-9f2a-1d4c8e5b6a70", "a v4 parses fine and does not sort")]
    [InlineData("0198f3a2-7c41-7b3e-0f2a-1d4c8e5b6a70", "no RFC 4122 variant")]
    [InlineData("not-a-uuid", "not a uuid at all")]
    [InlineData("0198f3a2-7c41-7b3e-9f2a-1d4c8e5b6a7", "a nibble short")]
    public void An_id_spelled_any_other_way_is_reported(string id, string why)
    {
        // Every one of these is a real id in some other system's spelling, and every one of them
        // compares unequal to the id it is. A reader storing one has stored a name that will never
        // match the line it came from.
        Assert.False(string.IsNullOrEmpty(why));
        AssertBreaks(ConformanceRule.EventIdShape, Line(id: id));
    }

    [Fact]
    public void An_id_that_is_not_even_a_string_is_reported()
    {
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Id":12345}
        """;

        AssertBreaks(ConformanceRule.EventIdShape, line);
    }

    [Theory]
    [InlineData("0198f3a2-7c41-7b3e-8f2a-1d4c8e5b6a70")]
    [InlineData("0198f3a2-7c41-7b3e-9f2a-1d4c8e5b6a70")]
    [InlineData("0198f3a2-7c41-7b3e-af2a-1d4c8e5b6a70")]
    [InlineData("0198f3a2-7c41-7b3e-bf2a-1d4c8e5b6a70")]
    [InlineData("00000000-0000-7000-8000-000000000000")]
    public void Every_variant_nibble_the_format_defines_is_accepted(string id)
    {
        // All four spellings of the RFC 4122 variant are legal, and the bash writer reaches all four:
        // it ORs 0x8000 over fourteen random bits. A check accepting only the one .NET happened to
        // produce would go red on the engine's own lines, sporadically.
        Assert.Empty(Check(Line(id: id)));
    }

    [Fact]
    public void A_null_id_is_absence_and_not_a_shape_fault()
    {
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Id":null}
        """;

        Assert.Empty(Check(line));
    }

    [Fact]
    public void An_empty_id_is_reported_once_as_the_absence_fault_it_is()
    {
        // Two findings for one defect makes a report harder to read and tells nobody anything more,
        // so the shape check stands aside for the one that already names it.
        string line = """
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","Id":""}
        """;

        ConformanceFinding finding = Assert.Single(Check(line));
        Assert.Equal(ConformanceRule.AbsentSpelling, finding.Rule);
    }

    [Fact]
    public void The_writer_and_the_shape_check_agree_on_what_an_id_is()
    {
        // Pinned to what the writer actually mints rather than to a literal, so the check cannot start
        // rejecting the ids every .NET producer on the fleet is writing.
        for (int i = 0; i < 64; i++)
            Assert.True(JournalConformance.IsWellFormedEventId(Guid.CreateVersion7().ToString("d")));

        Assert.False(JournalConformance.IsWellFormedEventId(Guid.NewGuid().ToString("d")));
        Assert.False(JournalConformance.IsWellFormedEventId(null));
        Assert.False(JournalConformance.IsWellFormedEventId(string.Empty));
    }

    [Theory]
    [InlineData("OpId")]
    [InlineData("RunId")]
    [InlineData("During")]
    public void A_reserved_field_is_part_of_the_contract_whether_or_not_anything_writes_it(string field)
    {
        string line = $$"""
        {"V":1,"EventType":"a_b","Data":{},"Timestamp":"2026-08-16T10:04:37.799Z","{{field}}":"x"}
        """;

        Assert.Empty(Check(line));
    }

    // ── journal.producer-matches-directory ───────────────────────────────────────────────────────

    [Fact]
    public void A_journal_a_reader_would_file_under_another_name_is_reported()
    {
        string directory = JournalLayout.DirectoryFor("kgsm-monitor", _root);
        Directory.CreateDirectory(directory);

        JournalReport report = JournalConformance.CheckJournal("kgsm-api", directory);

        ConformanceFinding finding = Assert.Single(report.Findings);
        Assert.Equal(ConformanceRule.ProducerMatchesDirectory, finding.Rule);
        Assert.Contains("kgsm-monitor", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_journal_where_a_reader_expects_it_is_not_reported()
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);

        Assert.Empty(JournalConformance.CheckJournal(Producer, directory).Findings);
    }

    [Fact]
    public void A_journal_at_a_configured_path_is_the_callers_word_and_not_a_finding()
    {
        // The engine's directory is configurable and need not sit under a state root at all. A scan
        // cannot find it, so a consumer names it — and a named journal has no directory to disagree
        // with, which is different from one that disagrees.
        string directory = Path.Combine(_root, "somewhere", "else");
        Directory.CreateDirectory(directory);

        Assert.Null(JournalLayout.ProducerOf(directory));
        Assert.Empty(JournalConformance.CheckJournal("kgsm", directory).Findings);
    }

    // ── journal.segment-name ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("latest.ndjson")]
    [InlineData("events.ndjson")]
    [InlineData("2026-8-16.ndjson")]
    [InlineData("2026-08-16.1.ndjson")]
    public void A_segment_retention_cannot_age_by_its_name_is_reported(string name)
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), Line() + "\n");

        JournalReport report = JournalConformance.CheckJournal(Producer, directory);

        Assert.Contains(report.Findings, f => f.Rule == ConformanceRule.SegmentName);
    }

    [Fact]
    public void A_file_that_is_not_a_segment_at_all_is_left_alone()
    {
        // Retention deliberately does not touch it, so neither does this: the directory belongs to the
        // producer, which is a reason to be careful with what is in it rather than to police it.
        string directory = Segments("2026-08-16");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "not a journal");

        Assert.Empty(JournalConformance.CheckJournal(Producer, directory).Findings);
    }

    // ── what was actually read ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_producer_that_has_recorded_nothing_reads_as_nothing_rather_than_as_a_pass()
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);

        JournalReport report = JournalConformance.CheckJournal(Producer, directory);

        // Both halves matter. There is nothing wrong with the journal, and there is also nothing in it
        // — a caller that cannot tell those apart has learned nothing from a clean report.
        Assert.True(report.Conforms);
        Assert.Equal(0, report.LinesChecked);
        Assert.Null(report.Segment);
    }

    [Fact]
    public void A_journal_that_is_not_there_is_absent_rather_than_broken()
    {
        JournalReport report = JournalConformance.CheckJournal(
            Producer, JournalLayout.DirectoryFor(Producer, _root));

        Assert.True(report.Conforms);
        Assert.Equal(0, report.LinesChecked);
    }

    [Fact]
    public void The_newest_segment_is_the_one_read()
    {
        // An old line records what an old build wrote, and is allowed to look old. Checking a host
        // means checking what the builds on it are writing now.
        string directory = Segments("2026-08-14", "2026-08-15", "2026-08-16");
        File.WriteAllText(Path.Combine(directory, "2026-08-14.ndjson"), Line(producerVersion: "2.3.0.0") + "\n");

        JournalReport report = JournalConformance.CheckJournal(Producer, directory);

        Assert.True(report.Conforms);
        Assert.Equal("2026-08-16.ndjson", report.Segment);
    }

    [Fact]
    public void A_finding_names_the_line_number_an_editor_jumps_to()
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);

        File.WriteAllLines(
            Path.Combine(directory, "2026-08-16.ndjson"),
            [Line(), Line(), Line(producerVersion: "2.3.0.0")]);

        JournalReport report = JournalConformance.CheckJournal(Producer, directory);

        ConformanceFinding finding = Assert.Single(report.Findings);
        Assert.Equal(3, finding.Line);
        Assert.Equal("2026-08-16.ndjson", finding.Segment);
    }

    [Fact]
    public void Reading_more_lines_reads_more_lines()
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);

        File.WriteAllLines(
            Path.Combine(directory, "2026-08-16.ndjson"),
            [Line(producerVersion: "2.3.0.0"), Line(), Line()]);

        Assert.True(JournalConformance.CheckJournal(Producer, directory, lines: 1).Conforms);
        Assert.False(JournalConformance.CheckJournal(Producer, directory, lines: 3).Conforms);
    }

    // ── host.hostname-agrees ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Producers_that_disagree_about_which_host_they_are_on_are_reported()
    {
        // Conformant in each journal read alone, and wrong the moment they are merged — which is how
        // every consumer reads them. This is the finding that only exists across producers.
        JournalTarget one = Journal("kgsm-monitor", Line(hostname: "hotrod"));
        JournalTarget two = Journal("kgsm-watchdog", Line(hostname: "HOTROD"));

        HostReport report = JournalConformance.CheckHost([one, two]);

        Assert.All(report.CrossJournal, f => Assert.Equal(ConformanceRule.HostnameAgrees, f.Rule));
        Assert.Equal(2, report.CrossJournal.Count);
        Assert.Contains("kgsm-monitor=hotrod", report.CrossJournal[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Producers_that_agree_are_not_reported()
    {
        HostReport report = JournalConformance.CheckHost(
            [Journal("kgsm-monitor", Line(hostname: "hotrod")), Journal("kgsm-watchdog", Line(hostname: "hotrod"))]);

        Assert.True(report.Conforms);
    }

    [Fact]
    public void A_producer_that_names_no_host_is_not_disagreeing_with_one_that_does()
    {
        // Hostname is optional. Absence is not a competing answer, and treating it as one would report
        // a producer for exercising a choice the envelope gives it.
        HostReport report = JournalConformance.CheckHost(
            [Journal("kgsm-monitor", Line(hostname: null)), Journal("kgsm-watchdog", Line(hostname: "hotrod"))]);

        Assert.True(report.Conforms);
    }

    // ── the report ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_clean_report_says_what_it_read()
    {
        // A clean report over nothing looks exactly like a clean report over a host. Telling those two
        // apart is the whole reason the check exists, so the coverage is in the text either way.
        HostReport report = JournalConformance.CheckHost(
            [Journal("kgsm-monitor", Line()), Journal("kgsm-watchdog", null)]);

        string description = report.Describe();

        Assert.Contains("kgsm-monitor(1)", description, StringComparison.Ordinal);
        Assert.Contains("kgsm-watchdog(0)", description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_report_locates_every_finding()
    {
        HostReport report = JournalConformance.CheckHost(
            [Journal("kgsm-monitor", Line(producerVersion: "2.3.0.0"))]);

        string description = report.Describe();

        Assert.Contains(ConformanceRule.ProducerVersionShape, description, StringComparison.Ordinal);
        Assert.Contains("kgsm-monitor/2026-08-16.ndjson:1", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_rule_the_checker_can_report_is_one_this_suite_exercises()
    {
        // The catalog and the coverage cannot drift apart: a rule added without a test here fails this
        // one, which is the only way "producer-agnostic by construction" stays true of the tests too.
        string[] declared = [.. typeof(ConformanceRule)
            .GetFields()
            .Where(static f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(static f => (string)f.GetRawConstantValue()!)];

        Assert.Equal(17, declared.Length);
        Assert.All(declared, rule => Assert.Contains(rule, ExercisedRules));
    }

    private static readonly HashSet<string> ExercisedRules =
    [
        ConformanceRule.LineParses,
        ConformanceRule.SchemaVersion,
        ConformanceRule.EventType,
        ConformanceRule.Data,
        ConformanceRule.Timestamp,
        ConformanceRule.AbsentSpelling,
        ConformanceRule.Actor,
        ConformanceRule.ProducerVersionShape,
        ConformanceRule.Severity,
        ConformanceRule.Outcome,
        ConformanceRule.Summary,
        ConformanceRule.EventIdShape,
        ConformanceRule.UnknownField,
        ConformanceRule.ProducerMatchesDirectory,
        ConformanceRule.SegmentName,
        ConformanceRule.JournalReadable,
        ConformanceRule.HostnameAgrees,
    ];

    // ── journal.readable ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_journal_that_cannot_be_read_is_reported_rather_than_read_as_empty()
    {
        // The failure mode this whole layer exists to keep out: a directory that cannot be entered
        // yields no journal, which is indistinguishable from a producer that recorded nothing.
        Assert.False(
            Environment.IsPrivilegedProcess,
            "a privileged process reads a mode-000 file anyway, so this cannot be measured as root");

        string directory = Segments("2026-08-16");
        string segment = Path.Combine(directory, "2026-08-16.ndjson");

        File.SetUnixFileMode(segment, UnixFileMode.None);

        try
        {
            JournalReport report = JournalConformance.CheckJournal(Producer, directory);

            Assert.Contains(report.Findings, f => f.Rule == ConformanceRule.JournalReadable);
            Assert.Equal(0, report.LinesChecked);
        }
        finally
        {
            File.SetUnixFileMode(segment, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // ── What the filesystem left behind ─────────────────────────────────────────────────

    /// <summary>
    /// A conforming line the machine died in front of is still a conforming line. The zeros ahead of
    /// it belong to the filesystem, and a check that read them as the producer's spelling would
    /// report a component for bytes it never wrote.
    /// </summary>
    [Fact]
    public void A_line_written_against_a_crash_hole_conforms()
    {
        Assert.Empty(Check(new string('\0', 200) + Line()));
    }

    /// <summary>A hole carrying nothing else is named for what it is, not judged as a line.</summary>
    [Fact]
    public void A_hole_carrying_nothing_else_is_reported_as_a_hole()
    {
        ConformanceFinding finding = AssertBreaks(ConformanceRule.LineParses, new string('\0', 200));

        Assert.Contains("unclean shutdown", finding.Detail, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ConformanceFinding> Check(string line) =>
        JournalConformance.CheckLine(Producer, "2026-08-16.ndjson", 1, line);

    private static ConformanceFinding AssertBreaks(string rule, string line)
    {
        IReadOnlyList<ConformanceFinding> findings = Check(line);

        ConformanceFinding? match = findings.FirstOrDefault(f => f.Rule == rule);

        Assert.True(
            match is not null,
            $"expected {rule}, got: {(findings.Count == 0 ? "nothing" : string.Join("; ", findings))}");

        return match!;
    }

    /// <summary>One envelope, conforming unless a caller breaks a specific part of it.</summary>

    // ── What a producer says about its own event ────────────────────────────────────────
    //
    // Spelling only. Whether a PARTICULAR event deserves a particular weight is the producer's
    // judgement, and a rule asserting it here would put one producer's policy inside a package every
    // other producer compiles against. Each rule therefore needs both halves: that it catches a
    // spelling nothing defines, and that it stays quiet for every spelling that is defined.

    [Theory]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("danger")]
    public void Severity_DoesNotFire_ForADefinedSpelling(string spelling)
    {
        Assert.DoesNotContain(
            Check(Line(version: "2", severity: spelling)),
            f => f.Rule == ConformanceRule.Severity);
    }

    [Fact]
    public void Severity_Fires_WhenTheSpellingIsNotOneTheScaleDefines()
    {
        // The live instance of this: one leaf writing "warning" where every other writes "warn".
        ConformanceFinding finding = Assert.Single(
            Check(Line(version: "2", severity: "warning")),
            f => f.Rule == ConformanceRule.Severity);

        Assert.Contains("warning", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Severity_DoesNotFire_WhenAbsent()
    {
        // Absent is a producer that has not started saying, which is quiet rather than malformed.
        Assert.DoesNotContain(Check(Line(version: "2")), f => f.Rule == ConformanceRule.Severity);
    }

    [Fact]
    public void Severity_DoesNotFire_WhenExplicitlyNull()
    {
        Assert.DoesNotContain(
            Check(Line(version: "2", severity: "null")), f => f.Rule == ConformanceRule.Severity);
    }

    [Theory]
    [InlineData("neutral")]
    [InlineData("success")]
    [InlineData("failure")]
    public void Outcome_DoesNotFire_ForADefinedSpelling(string spelling)
    {
        Assert.DoesNotContain(
            Check(Line(version: "2", outcome: spelling)),
            f => f.Rule == ConformanceRule.Outcome);
    }

    [Fact]
    public void Outcome_Fires_WhenTheSpellingIsNotOneTheSetDefines()
    {
        Assert.Single(
            Check(Line(version: "2", outcome: "ok")), f => f.Rule == ConformanceRule.Outcome);
    }

    [Fact]
    public void Summary_DoesNotFire_ForALineOfProse()
    {
        Assert.DoesNotContain(
            Check(Line(version: "2", summary: "uninstalled factorio")),
            f => f.Rule == ConformanceRule.Summary);
    }

    [Fact]
    public void Summary_Fires_WhenItIsNotText()
    {
        // The only thing a reader needs from this field is that it can be put on a screen. What it
        // SAYS is content, and belongs to whoever raised the event.
        string line = Line(version: "2").Replace("\"Data\"", "\"Summary\":42,\"Data\"", StringComparison.Ordinal);

        Assert.Single(Check(line), f => f.Rule == ConformanceRule.Summary);
    }

    [Fact]
    public void SchemaVersion_DoesNotFire_ForAnEarlierEnvelopeStillOnDisk()
    {
        // A line records what the build that wrote it produced, and retention holds it for months. A
        // reader that understood only the newest version would report a host's whole history broken
        // the day a field was added.
        Assert.DoesNotContain(Check(Line(version: "1")), f => f.Rule == ConformanceRule.SchemaVersion);
    }

    [Fact]
    public void SchemaVersion_Fires_ForAVersionNoReaderHereUnderstands()
    {
        Assert.Single(Check(Line(version: "99")), f => f.Rule == ConformanceRule.SchemaVersion);
    }

    private static string Line(
        string version = "1",
        string eventType = "server.ready",
        string data = """{"instance":"minecraft-01"}""",
        string timestamp = "2026-08-16T10:04:37.799Z",
        string? actor = "system:monitor",
        string? hostname = "hotrod",
        string? producerVersion = "2.7.1",
        string? id = null,
        string? severity = null,
        string? outcome = null,
        string? summary = null)
    {
        var fields = new List<string>
        {
            $"\"V\":{version}",
            $"\"EventType\":\"{eventType}\"",
            $"\"Data\":{data}",
            $"\"Timestamp\":\"{timestamp}\"",
        };

        if (actor is not null)
            fields.Add($"\"Actor\":\"{actor}\"");

        if (hostname is not null)
            fields.Add($"\"Hostname\":\"{hostname}\"");

        if (producerVersion is not null)
            fields.Add($"\"ProducerVersion\":\"{producerVersion}\"");

        if (id is not null)
            fields.Add($"\"Id\":\"{id}\"");

        if (severity is not null)
            fields.Add(severity == "null" ? "\"Severity\":null" : $"\"Severity\":\"{severity}\"");

        if (outcome is not null)
            fields.Add(outcome == "null" ? "\"Outcome\":null" : $"\"Outcome\":\"{outcome}\"");

        if (summary is not null)
            fields.Add(summary == "null" ? "\"Summary\":null" : $"\"Summary\":\"{summary}\"");

        return "{" + string.Join(",", fields) + "}";
    }

    /// <summary>A journal directory holding one empty segment per date given.</summary>
    private string Segments(params string[] dates)
    {
        string directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(directory);

        foreach (string date in dates)
            File.WriteAllText(Path.Combine(directory, $"{date}.ndjson"), Line() + "\n");

        return directory;
    }

    /// <summary>A journal for <paramref name="producer"/> holding one line, or none.</summary>
    private JournalTarget Journal(string producer, string? line)
    {
        string directory = JournalLayout.DirectoryFor(producer, _root);
        Directory.CreateDirectory(directory);

        if (line is not null)
            File.WriteAllText(Path.Combine(directory, "2026-08-16.ndjson"), line + "\n");

        return new JournalTarget(producer, directory);
    }
}
