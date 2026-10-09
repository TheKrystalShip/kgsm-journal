using System.Text;

using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Lifecycle;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Reading back what a producer's own journal last said about itself.
/// </summary>
/// <remarks>
/// The memory a process that exits does not have. Measured on the speech daemon: it reported a model
/// it could not load, exited when idle, woke with the model fixed, and wrote no recovery — because the
/// fresh process had never seen the fault.
/// </remarks>
public sealed class LeafStateTests : IDisposable
{
    private const string Producer = "tks-speech";

    private readonly string _root;
    private readonly string _directory;

    public LeafStateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kgsm-leaf-state", Path.GetRandomFileName());
        _directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(_directory);
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

    [Fact]
    public void A_fault_with_no_recovery_after_it_is_still_broken()
    {
        Write(Degraded("hearing"), Degraded("speaking"), Recovered("speaking"));

        Assert.Equal(["hearing"], LeafState.DegradedComponents(_directory).Order());
    }

    [Fact]
    public void A_fault_that_was_recovered_is_not_carried_forward()
    {
        Write(Degraded("backend"), Recovered("backend"));

        Assert.Empty(LeafState.DegradedComponents(_directory));
    }

    [Fact]
    public void A_fault_reported_again_after_a_recovery_is_still_broken()
    {
        Write(Degraded("backend"), Recovered("backend"), Degraded("backend"));

        Assert.Equal(["backend"], LeafState.DegradedComponents(_directory));
    }

    [Fact]
    public void A_leaf_coming_up_wipes_the_slate()
    {
        // The line that separates the two kinds of leaf. A resident one writes leaf_ready on every
        // start, so everything before it described a run that has ended and its faults must not be
        // carried into a fresh process. A leaf that exits when idle writes no ready line, which is
        // exactly why its faults do carry.
        Write(Degraded("sampling"), Ready(), Degraded("net-meter"));

        Assert.Equal(["net-meter"], LeafState.DegradedComponents(_directory));
    }

    [Fact]
    public void Only_the_newest_segment_is_read()
    {
        // A fault that predates the segment boundary is not carried over. The alternative is opening
        // older files on every start of a leaf that may start dozens of times a day, to recover a
        // fault nothing has re-observed since midnight.
        File.WriteAllLines(Path.Combine(_directory, "2026-08-15.ndjson"), [Degraded("yesterday")]);
        File.WriteAllLines(Path.Combine(_directory, "2026-08-16.ndjson"), [Degraded("today")]);

        Assert.Equal(["today"], LeafState.DegradedComponents(_directory));
    }

    [Fact]
    public void An_ordinary_event_from_this_producer_changes_nothing()
    {
        Write(
            Degraded("hearing"),
            """{"V":1,"EventType":"server.started","Data":{"InstanceName":"a"},"Timestamp":"2026-08-16T10:00:00.000Z"}""");

        Assert.Equal(["hearing"], LeafState.DegradedComponents(_directory));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"V":1,"EventType":"leaf.degraded","Data":{},"Timestamp":"2026-08-16T10:00:00.000Z"}""")]
    [InlineData("""{"V":1,"EventType":"leaf.degraded","Data":"x","Timestamp":"2026-08-16T10:00:00.000Z"}""")]
    public void A_line_that_names_no_component_says_nothing_about_the_state(string line)
    {
        Write(line);

        Assert.Empty(LeafState.DegradedComponents(_directory));
    }

    [Fact]
    public void A_journal_that_is_not_there_seeds_nothing()
    {
        // A state that cannot be read is not evidence of a fault, and half a replay would be worse
        // than none — it would carry faults forward past the recovery that cleared them.
        Assert.Empty(LeafState.DegradedComponents(Path.Combine(_root, "nowhere")));
        Assert.Empty(LeafState.DegradedComponents(null));
        Assert.Empty(LeafState.DegradedComponents("   "));
    }

    [Fact]
    public void A_producer_finds_its_own_journal_from_its_id()
    {
        Write(Degraded("hearing"));

        Assert.Equal(["hearing"], LeafState.DegradedComponentsFor(Producer, _root));
    }

    [Fact]
    public void A_report_carries_what_the_leaf_said_about_a_fault_and_when()
    {
        Write("""{"V":2,"EventType":"leaf.degraded","Data":{"Component":"upnp-router","Detail":"The router has not answered."},"Timestamp":"2026-08-16T04:45:00.000Z"}""");

        LeafDegradation only = Assert.Single(LeafState.Read(_directory).Degraded);
        Assert.Equal("upnp-router", only.Component);
        Assert.Equal("The router has not answered.", only.Detail);
        Assert.Equal(new DateTimeOffset(2026, 8, 16, 4, 45, 0, TimeSpan.Zero), only.Since);
    }

    [Fact]
    public void A_fault_the_leaf_said_it_fixed_is_a_recovery()
    {
        Write(Degraded("backend"), Recovered("backend"));

        LeafStateReport report = LeafState.Read(_directory);

        Assert.Empty(report.Degraded);
        Assert.Equal(["backend"], report.Recovered);
        Assert.Empty(report.Cleared);
    }

    [Fact]
    public void A_fault_wiped_by_the_leaf_coming_up_is_cleared_and_not_a_recovery()
    {
        // A daemon restarted in the middle of an outage writes a ready line and has not re-observed
        // anything yet. Reading that as a recovery would announce the outage over while it continues.
        Write(Degraded("upnp-router"), Ready());

        LeafStateReport report = LeafState.Read(_directory);

        Assert.Empty(report.Degraded);
        Assert.Empty(report.Recovered);
        Assert.Equal(["upnp-router"], report.Cleared);
    }

    [Fact]
    public void A_component_is_in_exactly_the_set_its_last_line_puts_it_in()
    {
        Write(Degraded("a"), Ready(), Degraded("a"), Degraded("b"), Recovered("b"), Degraded("c"), Ready(), Recovered("c"));

        LeafStateReport report = LeafState.Read(_directory);

        Assert.Empty(report.Degraded);
        Assert.Equal(["b", "c"], report.Recovered.Order());
        Assert.Equal(["a"], report.Cleared);
    }

    [Fact]
    public void A_component_the_segment_never_mentions_is_in_no_set()
    {
        // A fault reported before the segment boundary is neither broken, recovered nor cleared as far as
        // this read can tell, and a consumer must be able to see that it was told nothing.
        File.WriteAllLines(Path.Combine(_directory, "2026-08-15.ndjson"), [Degraded("yesterday")]);
        File.WriteAllLines(Path.Combine(_directory, "2026-08-16.ndjson"), [Degraded("today")]);

        LeafStateReport report = LeafState.Read(_directory);

        Assert.Equal(["today"], report.Degraded.Select(d => d.Component));
        Assert.Empty(report.Recovered);
        Assert.Empty(report.Cleared);
    }

    [Fact]
    public void A_fault_with_no_detail_or_timestamp_reports_both_as_unknown()
    {
        Write("""{"V":2,"EventType":"leaf.degraded","Data":{"Component":"hearing","Detail":null}}""");

        LeafDegradation only = Assert.Single(LeafState.Read(_directory).Degraded);
        Assert.Null(only.Detail);
        Assert.Null(only.Since);
    }

    /// <summary>
    /// A leaf that came back after the machine went down reports the restart it wrote, not the
    /// faults that restart wiped.
    /// </summary>
    /// <remarks>
    /// The segment this replays is the producer's newest, which after an unclean shutdown is exactly
    /// the one carrying the hole — and the ready line the fresh process wrote is the append sitting
    /// against it, because the hole ends where the next write began.
    /// </remarks>
    [Fact]
    public void A_ready_line_written_against_a_crash_hole_still_wipes_the_faults_before_it()
    {
        string path = Path.Combine(_directory, "2026-08-16.ndjson");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            byte[] fault = Encoding.UTF8.GetBytes(Degraded("hearing") + "\n");
            byte[] ready = Encoding.UTF8.GetBytes(Ready() + "\n");

            stream.Write(fault, 0, fault.Length);
            stream.Write(new byte[256], 0, 256);
            stream.Write(ready, 0, ready.Length);
        }

        LeafStateReport report = LeafState.Read(_directory);

        Assert.Empty(report.Degraded);
        Assert.Equal(["hearing"], report.Cleared);
    }

    private void Write(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_directory, "2026-08-16.ndjson"), lines);

    private static string Degraded(string component) => Line(LeafLifecycleEvents.Degraded, component);

    private static string Recovered(string component) => Line(LeafLifecycleEvents.Recovered, component);

    private static string Ready() =>
        """{"V":1,"EventType":"leaf.ready","Data":{"StartupMs":5},"Timestamp":"2026-08-16T10:00:00.000Z"}""";

    private static string Line(string type, string component) => $$"""
        {"V":1,"EventType":"{{type}}","Data":{"Component":"{{component}}"},"Timestamp":"2026-08-16T10:00:00.000Z"}
        """;
}
