using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Lifecycle;

/// <summary>
/// A leaf reporting its own state changes to its own journal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transitions, not states.</b> Every method here writes only when something actually changed, and
/// the change is decided from what this object has already reported rather than from what the caller
/// believes. A leaf may therefore call these from a polling loop, from an event handler, or from both,
/// without producing a line per poll — which is what lets a caller say what it observes instead of
/// tracking what it has said.
/// </para>
/// <para>
/// <b>The leaf writes no field names.</b> The payload is composed here, so these four events have
/// exactly one writer across the whole ecosystem however many leaves emit them. That is the point:
/// seven producers each spelling a payload by hand is the drift this layer exists to prevent, and it
/// is not hypothetical — it is already what a producer's threshold payload and the reader's class for
/// it are doing.
/// </para>
/// <para>
/// <b>Identity comes from <see cref="JournalRecorder"/> and is not overridden.</b> A leaf reporting on
/// itself was driven by no product surface, so the inherited <c>system:&lt;leaf&gt;</c> actor and
/// <c>system</c> origin are the true answers rather than defaults to accept. This is a separate
/// recorder from the leaf's own for that reason: a leaf whose ordinary events are person-driven
/// overrides both to null, and reusing that recorder would stamp a lifecycle event with no author.
/// </para>
/// <para>
/// <b>Nothing here throws and nothing here fails an operation.</b> The state change has already
/// happened by the time it is reported; refusing it because the record could not be written would
/// trade a missing line for broken behaviour.
/// </para>
/// </remarks>
public sealed class LeafLifecycle : JournalRecorder
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly DateTimeOffset? _startedAt;

    // One gate over the decision AND the write. Holding it across the append is deliberate: these
    // events are rare, and it makes the order of lines in the journal the order the transitions
    // happened in — which is the only thing a reader can use, since two appends racing would let a
    // recovery land before the degradation it ends.
    private readonly Lock _gate = new();

    private readonly Dictionary<string, DateTimeOffset> _degradedSince = new(StringComparer.Ordinal);
    private bool _ready;
    private bool _stopping;

    /// <summary>
    /// Initializes a lifecycle reporter for the producer the writer belongs to.
    /// </summary>
    /// <param name="writer">The leaf's own journal writer.</param>
    /// <param name="logger">The logger to use.</param>
    /// <param name="clock">The clock. Null uses the system clock.</param>
    /// <param name="startedAt">
    /// When the process started, read once. Null reads it from the OS. A source that answers null is
    /// the case where the OS would not say, and every duration is then omitted rather than estimated —
    /// so it is expressible rather than only reachable by the read failing.
    /// <para>
    /// <b>A leaf that replaces its own image must pass one.</b> An <c>execve</c> keeps the process
    /// id, so the OS goes on reporting the original start: a hot-swap of a daemon that had been up
    /// four hours reported a four-hour startup time. That is measured, not fabricated, and it is still
    /// the wrong clock. Capture a moment at the top of the entry point and pass it — correct for a
    /// cold start too, where it differs from the process start only by runtime init.
    /// </para>
    /// </param>
    /// <param name="degraded">
    /// What this producer already reported broken and has not reported fixed, so a transition can be
    /// measured against something this process did not itself observe.
    /// <para>
    /// <b>A leaf that exits between observations needs this.</b> Measured on a speech daemon: it
    /// reported a model it could not load, exited when idle, woke with the model fixed, and wrote no
    /// recovery — because the fresh process had never seen the fault. Seed it with
    /// <see cref="LeafState.DegradedComponentsFor"/> and both directions work across a restart. A
    /// resident leaf needs nothing here: its <c>leaf_ready</c> is the clean slate.
    /// </para>
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when the writer or logger is null.</exception>
    public LeafLifecycle(
        IEventJournalWriter writer,
        ILogger<LeafLifecycle> logger,
        Func<DateTimeOffset>? clock = null,
        Func<DateTimeOffset?>? startedAt = null,
        IEnumerable<string>? degraded = null)
        : base(writer, logger)
    {
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _startedAt = (startedAt ?? ProcessStart)();

        // Seeded with the moment this process began rather than when the fault was first seen: how
        // long a component was broken is only knowable by whoever watched it break, and a duration
        // measured from a restart would understate every one that outlived a process.
        foreach (string component in degraded ?? [])
        {
            if (!string.IsNullOrWhiteSpace(component))
                _degradedSince[component] = _startedAt ?? _clock();
        }
    }

    /// <summary>Whether this leaf has reported itself ready.</summary>
    public bool IsReady
    {
        get { lock (_gate) { return _ready; } }
    }

    /// <summary>The components currently reported degraded, in no particular order.</summary>
    public IReadOnlyCollection<string> DegradedComponents
    {
        get { lock (_gate) { return [.. _degradedSince.Keys]; } }
    }

    /// <summary>
    /// Reports that the leaf can now do its job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call this from the leaf's own readiness signal, never from the host's.</b> A generic host
    /// considers itself started once every hosted service has started, which is before a supervisor has
    /// joined its slice, before a gateway has connected, and before a sampler has produced a frame. The
    /// moment worth reporting is the one the leaf itself can name.
    /// </para>
    /// <para>
    /// Once per process. A leaf that later becomes unable reports <see cref="MarkDegraded"/>, which is
    /// a different fact from never having come up.
    /// </para>
    /// </remarks>
    /// <param name="detail">What it came up as, when that is worth saying. Optional.</param>
    /// <returns>True when a line was written; false when it was already ready, or the write failed.</returns>
    public bool MarkReady(string? detail = null)
    {
        lock (_gate)
        {
            if (_ready)
                return false;

            _ready = true;

            long? startupMs = _startedAt is { } start
                ? (long)Math.Max(0, (_clock() - start).TotalMilliseconds)
                : null;

            return Record(LeafLifecycleEvents.Names.Ready, w =>
            {
                WriteNullableNumber(w, LeafLifecycleFields.StartupMs, startupMs);
                WriteNullable(w, LeafLifecycleFields.Detail, detail);
            });
        }
    }

    /// <summary>
    /// Reports that one part of the leaf's job has stopped working.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="component"/> is the dedup key and the reason this is not a boolean: a leaf can
    /// be broken in two ways at once and recover from one of them, and "the assistant is degraded"
    /// sends somebody reading logs where "the assistant's LLM backend is unreachable" does not.
    /// </para>
    /// <para>
    /// <b>A component already degraded is a no-op, even when <paramref name="detail"/> differs.</b>
    /// The first report is the transition; a reason that changes while the component stays broken is
    /// not a second one, and emitting it would turn one fault into a stream.
    /// </para>
    /// <para>
    /// Keep the set of components <b>bounded and known to the leaf</b>. A component id built from
    /// something the host supplies — a guild, a mount, an instance — makes this dictionary grow without
    /// limit; name the class of thing and put the offenders in <paramref name="detail"/>.
    /// </para>
    /// <para>
    /// This may be called before <see cref="MarkReady"/>. A leaf that comes up already unable to do
    /// part of its job is reporting honestly, and refusing the order would lose that.
    /// </para>
    /// </remarks>
    /// <param name="component">Which part of the job. Bounded, stable, lowercase-with-dashes by convention.</param>
    /// <param name="detail">What is wrong, in a sentence somebody can act on.</param>
    /// <returns>True when a line was written; false when it was already degraded, or the write failed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="component"/> is blank.</exception>
    public bool MarkDegraded(string component, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component, nameof(component));

        lock (_gate)
        {
            if (_degradedSince.ContainsKey(component))
                return false;

            _degradedSince[component] = _clock();

            return Record(LeafLifecycleEvents.Names.Degraded, w =>
            {
                w.WriteString(LeafLifecycleFields.Component, component);
                WriteNullable(w, LeafLifecycleFields.Detail, detail);
            });
        }
    }

    /// <summary>
    /// Reports that a part that was not working is working again.
    /// </summary>
    /// <remarks>
    /// <b>A component that was never reported degraded writes nothing.</b> A recovery for something
    /// that never broke is a transition that did not happen, and a consumer clearing an alert it never
    /// raised is the mildest of the things that follow from inventing one.
    /// </remarks>
    /// <param name="component">The component named when it was reported degraded.</param>
    /// <returns>True when a line was written; false when it was not degraded, or the write failed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="component"/> is blank.</exception>
    public bool MarkRecovered(string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component, nameof(component));

        lock (_gate)
        {
            if (!_degradedSince.Remove(component, out DateTimeOffset since))
                return false;

            long degradedForSec = (long)Math.Max(0, (_clock() - since).TotalSeconds);

            return Record(LeafLifecycleEvents.Names.Recovered, w =>
            {
                w.WriteString(LeafLifecycleFields.Component, component);
                w.WriteNumber(LeafLifecycleFields.DegradedForSec, degradedForSec);
            });
        }
    }

    /// <summary>
    /// Reports that the leaf is going away on purpose.
    /// </summary>
    /// <remarks>
    /// The last thing the leaf can say, and the one that tells a consumer this was a deploy rather than
    /// a fault. Nothing confirms it afterwards — see <see cref="LeafLifecycleEvents.Stopping"/>.
    /// </remarks>
    /// <param name="reason">Why. One of <see cref="LeafStopReason"/> unless the leaf means something else.</param>
    /// <returns>True when a line was written; false when it already said so, or the write failed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is blank.</exception>
    public bool MarkStopping(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));

        lock (_gate)
        {
            if (_stopping)
                return false;

            _stopping = true;

            long? uptimeSec = _startedAt is { } start
                ? (long)Math.Max(0, (_clock() - start).TotalSeconds)
                : null;

            return Record(LeafLifecycleEvents.Names.Stopping, w =>
            {
                w.WriteString(LeafLifecycleFields.Reason, reason);
                WriteNullableNumber(w, LeafLifecycleFields.UptimeSec, uptimeSec);
            });
        }
    }

    /// <summary>Writes a number, or a real JSON null when there is none.</summary>
    /// <remarks>
    /// Absent and null are the same thing to a reader, so a duration nobody could measure is spelled as
    /// the absence of one rather than as a zero that reads like a measurement.
    /// </remarks>
    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } number)
            writer.WriteNumber(name, number);
        else
            writer.WriteNull(name);
    }

    /// <summary>
    /// When this process started, or null when the OS would not say.
    /// </summary>
    /// <remarks>
    /// The process rather than this object: an operator asking how long a leaf took to come up means
    /// from <c>exec</c>, not from whenever a container happened to construct the reporter. Reading it
    /// is a <c>/proc</c> lookup on Linux and needs no reflection, so it survives publishing ahead of
    /// time. A failure yields null and the duration fields are simply not reported.
    /// </remarks>
    private static DateTimeOffset? ProcessStart()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            return new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
