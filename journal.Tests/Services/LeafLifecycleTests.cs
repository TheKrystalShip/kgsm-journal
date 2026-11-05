using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Lifecycle;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for a leaf reporting its own state changes.
/// </summary>
/// <remarks>
/// The theme is that this reports <em>transitions</em>, not states. A leaf is expected to call these
/// from a polling loop or an event handler without tracking what it has already said, so the whole
/// value of the class is in what it declines to write — and that is what most of these check.
/// </remarks>
public sealed class LeafLifecycleTests : IDisposable
{
    private const string Producer = "kgsm-watchdog";

    private readonly string _root;
    private readonly string _directory;
    private DateTimeOffset _now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    public LeafLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kgsm-lifecycle-tests", Path.GetRandomFileName());
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

    // ── ready ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_leaf_reports_that_it_came_up()
    {
        LeafLifecycle lifecycle = Build(StartedAgo(TimeSpan.FromSeconds(3)));

        Assert.True(lifecycle.MarkReady("in-slice"));
        Assert.True(lifecycle.IsReady);

        JsonElement data = SingleEvent(LeafLifecycleEvents.Ready);
        Assert.Equal(3000, data.GetProperty(LeafLifecycleFields.StartupMs).GetInt64());
        Assert.Equal("in-slice", data.GetProperty(LeafLifecycleFields.Detail).GetString());
    }

    [Fact]
    public void A_leaf_says_it_came_up_once()
    {
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkReady());
        Assert.False(lifecycle.MarkReady());
        Assert.False(lifecycle.MarkReady("again"));

        Assert.Single(Lines());
    }

    [Fact]
    public void A_startup_time_nobody_could_measure_is_not_invented()
    {
        // The process start is read from the OS and can fail. A zero would read like a measurement of
        // an instant startup, which is a different claim from having no measurement.
        LeafLifecycle lifecycle = Build(StartUnknown);

        lifecycle.MarkReady();

        JsonElement data = SingleEvent(LeafLifecycleEvents.Ready);
        Assert.Equal(JsonValueKind.Null, data.GetProperty(LeafLifecycleFields.StartupMs).ValueKind);
    }

    // ── degraded / recovered ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_leaf_reports_a_part_of_its_job_that_stopped_working()
    {
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkDegraded("cgroup-delegation", "could not enable controllers"));

        JsonElement data = SingleEvent(LeafLifecycleEvents.Degraded);
        Assert.Equal("cgroup-delegation", data.GetProperty(LeafLifecycleFields.Component).GetString());
        Assert.Equal("could not enable controllers", data.GetProperty(LeafLifecycleFields.Detail).GetString());
        Assert.Equal(["cgroup-delegation"], lifecycle.DegradedComponents);
    }

    [Fact]
    public void A_component_already_broken_is_not_reported_broken_again()
    {
        // The point of the class. A leaf polling its own health calls this every tick; the first call
        // is the transition and the rest are the same fault still being true.
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkDegraded("net-meter", "bpf map pin absent"));
        Assert.False(lifecycle.MarkDegraded("net-meter", "bpf map pin absent"));
        Assert.False(lifecycle.MarkDegraded("net-meter", "bpf map pin absent"));

        Assert.Single(Lines());
    }

    [Fact]
    public void A_reason_that_changes_while_the_fault_persists_is_not_a_new_fault()
    {
        // Deliberate. A backend that reports a different error string each time it is retried would
        // otherwise turn one outage into a stream of them.
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkDegraded("backend", "connection refused"));
        Assert.False(lifecycle.MarkDegraded("backend", "timed out"));

        Assert.Single(Lines());
        Assert.Equal("connection refused", SingleEvent(LeafLifecycleEvents.Degraded)
            .GetProperty(LeafLifecycleFields.Detail).GetString());
    }

    [Fact]
    public void A_leaf_reports_a_part_that_works_again_and_how_long_it_did_not()
    {
        LeafLifecycle lifecycle = Build();

        lifecycle.MarkDegraded("watchdog", "socket unreachable");
        _now = _now.AddMinutes(4);

        Assert.True(lifecycle.MarkRecovered("watchdog"));
        Assert.Empty(lifecycle.DegradedComponents);

        JsonElement data = EventAt(1, LeafLifecycleEvents.Recovered);
        Assert.Equal("watchdog", data.GetProperty(LeafLifecycleFields.Component).GetString());
        Assert.Equal(240, data.GetProperty(LeafLifecycleFields.DegradedForSec).GetInt64());
    }

    [Fact]
    public void A_recovery_for_something_that_never_broke_is_a_transition_that_did_not_happen()
    {
        // Nothing is written, because nothing changed. A consumer clearing an alert it never raised is
        // the mildest consequence of inventing one.
        LeafLifecycle lifecycle = Build();

        Assert.False(lifecycle.MarkRecovered("never-broken"));
        Assert.Empty(Lines());
    }

    [Fact]
    public void A_recovery_is_reported_once_and_a_repeat_is_not()
    {
        LeafLifecycle lifecycle = Build();

        lifecycle.MarkDegraded("gateway", "disconnected");

        Assert.True(lifecycle.MarkRecovered("gateway"));
        Assert.False(lifecycle.MarkRecovered("gateway"));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Two_components_break_and_recover_independently()
    {
        // Why degradation is a component and not a boolean: a leaf can be broken in two ways at once
        // and fixing one of them is not the leaf being well.
        LeafLifecycle lifecycle = Build();

        lifecycle.MarkDegraded("gateway", "disconnected");
        lifecycle.MarkDegraded("guild-store", "could not open");

        Assert.Equal(2, lifecycle.DegradedComponents.Count);

        Assert.True(lifecycle.MarkRecovered("gateway"));
        Assert.Equal(["guild-store"], lifecycle.DegradedComponents);
        Assert.False(lifecycle.MarkRecovered("gateway"));
    }

    [Fact]
    public void A_component_can_break_again_after_recovering()
    {
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkDegraded("backend", "refused"));
        Assert.True(lifecycle.MarkRecovered("backend"));
        Assert.True(lifecycle.MarkDegraded("backend", "refused"));

        Assert.Equal(3, Lines().Length);
    }

    [Fact]
    public void A_leaf_that_comes_up_already_broken_may_say_so_first()
    {
        // Call order is not enforced. A leaf whose initialisation partly failed is reporting
        // honestly, and refusing the order would lose that.
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkDegraded("cuda", "no device; running on CPU"));
        Assert.True(lifecycle.MarkReady());

        Assert.Equal(
            [LeafLifecycleEvents.Degraded, LeafLifecycleEvents.Ready],
            Lines().Select(TypeOf).ToArray());
    }

    // ── state carried across a restart ───────────────────────────────────────────────────────────

    [Fact]
    public void A_leaf_that_wakes_healthy_after_reporting_a_fault_clears_it()
    {
        // The defect this seed exists for, measured on a speech daemon: it reported a model it could
        // not load, exited when idle, woke with the model fixed, and wrote no recovery — because the
        // fresh process had never seen the fault. A journal that reports a fault and can never clear
        // it is worse than one that reports neither.
        LeafLifecycle woken = Build(degraded: ["hearing"]);

        Assert.Equal(["hearing"], woken.DegradedComponents);
        Assert.True(woken.MarkRecovered("hearing"));

        Assert.Equal(LeafLifecycleEvents.Recovered, TypeOf(Assert.Single(Lines())));
    }

    [Fact]
    public void A_leaf_that_wakes_still_broken_says_nothing()
    {
        // The other half, and why the seed is not simply "report the state every wake": a condition
        // that has not changed is not a transition, however many processes observe it.
        LeafLifecycle woken = Build(degraded: ["backend"]);

        Assert.False(woken.MarkDegraded("backend", "still cannot apply"));
        Assert.Empty(Lines());
    }

    [Fact]
    public void A_seeded_fault_is_reported_recovered_only_once()
    {
        LeafLifecycle woken = Build(degraded: ["backend"]);

        Assert.True(woken.MarkRecovered("backend"));
        Assert.False(woken.MarkRecovered("backend"));

        Assert.Single(Lines());
    }

    [Fact]
    public void A_seeded_duration_is_measured_from_this_process_and_not_invented()
    {
        // How long a component was broken is only knowable by whoever watched it break. Measuring from
        // the restart understates it, which is honest; inventing an earlier moment would not be.
        LeafLifecycle woken = Build(StartedAgo(TimeSpan.FromMinutes(2)), degraded: ["backend"]);

        woken.MarkRecovered("backend");

        Assert.Equal(120, EventOf(Lines()[0], LeafLifecycleEvents.Recovered)
            .GetProperty(LeafLifecycleFields.DegradedForSec).GetInt64());
    }

    // ── stopping ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_leaf_reports_that_it_is_going_away_on_purpose()
    {
        LeafLifecycle lifecycle = Build(StartedAgo(TimeSpan.FromHours(2)));

        Assert.True(lifecycle.MarkStopping(LeafStopReason.Signal));

        JsonElement data = SingleEvent(LeafLifecycleEvents.Stopping);
        Assert.Equal("signal", data.GetProperty(LeafLifecycleFields.Reason).GetString());
        Assert.Equal(7200, data.GetProperty(LeafLifecycleFields.UptimeSec).GetInt64());
    }

    [Fact]
    public void A_leaf_says_goodbye_once()
    {
        LeafLifecycle lifecycle = Build();

        Assert.True(lifecycle.MarkStopping(LeafStopReason.Signal));
        Assert.False(lifecycle.MarkStopping(LeafStopReason.Signal));

        Assert.Single(Lines());
    }

    [Theory]
    [InlineData(LeafStopReason.Signal)]
    [InlineData(LeafStopReason.Idle)]
    [InlineData(LeafStopReason.Reload)]
    public void The_reason_reaches_the_line_unchanged(string reason)
    {
        // Load-bearing on the consumer side: `idle` is a socket-activated leaf's resting state and
        // `reload` is a leaf replacing itself without restarting what it supervises. Neither is an
        // outage, and both look like one without this field.
        Build().MarkStopping(reason);

        Assert.Equal(reason, SingleEvent(LeafLifecycleEvents.Stopping)
            .GetProperty(LeafLifecycleFields.Reason).GetString());
    }

    // ── identity ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_leaf_reporting_on_itself_is_the_author_and_no_surface_drove_it()
    {
        // Inherited from JournalRecorder rather than set here, and correct rather than a default: a
        // leaf that came up on its own really was driven by no product surface.
        Build().MarkReady();

        JsonElement line = Parse(Lines()[0]);
        Assert.Equal("system:watchdog", line.GetProperty("Actor").GetString());
        Assert.Equal("system", line.GetProperty("Origin").GetString());
    }

    [Fact]
    public void A_lifecycle_event_names_no_leaf_in_its_payload()
    {
        // The producer is established from the journal the line was read out of. A leaf id in the
        // payload would be a second answer able to disagree with it.
        Build().MarkReady();

        JsonElement data = SingleEvent(LeafLifecycleEvents.Ready);

        Assert.False(data.TryGetProperty("Leaf", out _));
        Assert.False(data.TryGetProperty("Producer", out _));
        Assert.False(data.TryGetProperty("Version", out _));
    }

    // ── input ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_component_that_names_nothing_is_refused(string component)
    {
        LeafLifecycle lifecycle = Build();

        Assert.Throws<ArgumentException>(() => lifecycle.MarkDegraded(component, "why"));
        Assert.Throws<ArgumentException>(() => lifecycle.MarkRecovered(component));
    }

    [Fact]
    public void A_stop_with_no_reason_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Build().MarkStopping("  "));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A lifecycle over a real writer in a temp journal, on a frozen clock.
    /// </summary>
    /// <param name="startedAt">
    /// When the process started. Defaults to the frozen clock's own moment; pass
    /// <see cref="StartUnknown"/> for the case where the OS would not say.
    /// </param>
    private LeafLifecycle Build(
        Func<DateTimeOffset?>? startedAt = null, IEnumerable<string>? degraded = null)
    {
        var options = new EventJournalWriterOptions
        {
            Producer = Producer,
            Directory = _directory,
            Hostname = "hotrod",
            ProducerVersion = "1.30.2+test",
            Clock = () => _now,
        };

        IEventJournalWriter writer = new EventJournalWriter(
            options, NullLogger<EventJournalWriter>.Instance);

        return new LeafLifecycle(
            writer,
            NullLogger<LeafLifecycle>.Instance,
            () => _now,
            startedAt ?? (() => _now),
            degraded);
    }

    /// <summary>A process start the OS would not report.</summary>
    private static readonly Func<DateTimeOffset?> StartUnknown = static () => null;

    /// <summary>A process that started <paramref name="ago"/> before the frozen clock's moment.</summary>
    private Func<DateTimeOffset?> StartedAgo(TimeSpan ago)
    {
        DateTimeOffset start = _now - ago;
        return () => start;
    }

    private string[] Lines() =>
        File.Exists(Segment) ? File.ReadAllLines(Segment) : [];

    private string Segment => Path.Combine(_directory, $"{_now:yyyy-MM-dd}.ndjson");

    private static JsonElement Parse(string line) => JsonDocument.Parse(line).RootElement.Clone();

    private static string TypeOf(string line) => Parse(line).GetProperty("EventType").GetString()!;

    private JsonElement SingleEvent(string expectedType)
    {
        string line = Assert.Single(Lines());
        return EventOf(line, expectedType);
    }

    private JsonElement EventAt(int index, string expectedType) =>
        EventOf(Lines()[index], expectedType);

    private static JsonElement EventOf(string line, string expectedType)
    {
        JsonElement root = Parse(line);
        Assert.Equal(expectedType, root.GetProperty("EventType").GetString());
        return root.GetProperty("Data");
    }
}
