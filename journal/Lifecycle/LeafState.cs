using System.Globalization;
using System.Text.Json;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Lifecycle;

/// <summary>
/// What a producer's own journal says it last reported about itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>A process reports transitions from what it remembers, and a process that exits remembers
/// nothing.</b> Measured on a speech daemon: it reported a model it could not load, exited when idle,
/// woke with the model fixed, and wrote no recovery — because the fresh process had never seen the
/// fault. A journal that reports a fault and can never clear it is worse than one that reports
/// neither.
/// </para>
/// <para>
/// The journal is already the record, so the memory a process lacks is a tail-read away. Seeding
/// <see cref="LeafLifecycle"/> from this makes the transition model hold across a restart: a leaf that
/// wakes healthy after reporting a fault clears it, and one that wakes still broken says nothing,
/// which is what a leaf that exits between observations needs in both directions.
/// </para>
/// <para>
/// Only the newest segment is read, and only its lifecycle lines. A fault that predates the segment
/// boundary is not carried over — the alternative is opening older files on every start of a leaf that
/// may start dozens of times a day, to recover a fault nothing has re-observed since midnight.
/// </para>
/// </remarks>
public static class LeafState
{
    /// <summary>
    /// The components this producer last said were broken and has not since said were fixed.
    /// </summary>
    /// <param name="journalDirectory">The producer's own journal directory.</param>
    /// <returns>
    /// The degraded component ids. Empty when the journal is absent, unreadable, or says nothing is
    /// broken — a state that cannot be read is not evidence of a fault.
    /// </returns>
    public static IReadOnlyCollection<string> DegradedComponents(string? journalDirectory) =>
        [.. Read(journalDirectory).Degraded.Select(d => d.Component)];

    /// <summary>
    /// The state a producer's own journal directory implies, from its producer id.
    /// </summary>
    /// <param name="producer">The producer id.</param>
    /// <param name="stateRoot">Where state directories live. Null uses the default.</param>
    /// <returns>The degraded component ids.</returns>
    /// <exception cref="ArgumentException">Thrown when the producer id is unusable.</exception>
    public static IReadOnlyCollection<string> DegradedComponentsFor(string producer, string? stateRoot = null) =>
        DegradedComponents(JournalLayout.DirectoryFor(producer, stateRoot));

    /// <summary>
    /// Everything the newest segment says about each component this producer has reported on: what is
    /// broken now and what the leaf said about it, and how each component that is not broken stopped
    /// being so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A component leaving the broken set is two different facts.</b> The leaf said it was fixed, or
    /// the leaf started again and its fresh process has not re-observed it. A consumer announcing a
    /// recovery needs to tell those apart, because the second one is not a recovery — a daemon restarted
    /// in the middle of an outage would otherwise announce the outage over.
    /// </para>
    /// <para>
    /// A component this segment never mentions appears in none of the three sets. That includes a fault
    /// reported before the segment boundary, which is exactly the case a consumer must not read as
    /// either outcome.
    /// </para>
    /// </remarks>
    /// <param name="journalDirectory">The producer's own journal directory.</param>
    /// <returns>
    /// The report. Empty when the journal is absent or unreadable — a state that cannot be read is not
    /// evidence of a fault, or of a recovery.
    /// </returns>
    public static LeafStateReport Read(string? journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory))
            return LeafStateReport.Empty;

        string[] segments;

        try
        {
            if (!Directory.Exists(journalDirectory))
                return LeafStateReport.Empty;

            segments = Directory.GetFiles(journalDirectory, "*.ndjson");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LeafStateReport.Empty;
        }

        if (segments.Length == 0)
            return LeafStateReport.Empty;

        Array.Sort(segments, StringComparer.Ordinal);

        var replay = new Replay();

        try
        {
            foreach (string line in File.ReadLines(segments[^1]))
                replay.Apply(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Half a replay is worse than none: it would carry faults forward past the recovery that
            // cleared them. An unreadable journal seeds nothing.
            return LeafStateReport.Empty;
        }

        return replay.ToReport();
    }

    /// <summary>The running state of one replay, line by line.</summary>
    /// <remarks>
    /// The three maps stay disjoint: every transition moves a component into exactly one of them, so a
    /// component's last line decides which one it is in.
    /// </remarks>
    private sealed class Replay
    {
        private readonly Dictionary<string, LeafDegradation> _degraded = new(StringComparer.Ordinal);
        private readonly HashSet<string> _recovered = new(StringComparer.Ordinal);
        private readonly HashSet<string> _cleared = new(StringComparer.Ordinal);

        public LeafStateReport ToReport() => new(
            [.. _degraded.Values],
            [.. _recovered],
            [.. _cleared]);

        /// <summary>Replays one line, ignoring anything that is not a transition.</summary>
        public void Apply(string line)
        {
            // The segment this replays is a producer's newest, which after an unclean shutdown is
            // the one holding the hole — with the ready line the fresh process wrote sitting
            // directly against it. Dropping that line reports a leaf as still broken over faults
            // its restart already wiped.
            line = JournalLine.WithoutHole(line);

            if (line.Length == 0)
                return;

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("EventType", out JsonElement typeElement)
                    || typeElement.ValueKind != JsonValueKind.String)
                {
                    return;
                }

                string? type = typeElement.GetString();

                // A leaf coming up is a fresh process with fresh observations, and everything before it
                // described a run that has ended. Resident leaves get their clean slate from this; a leaf
                // that exits when idle writes no ready line and carries its faults across, which is
                // exactly the difference between the two.
                if (type == LeafLifecycleEvents.Ready)
                {
                    foreach (string wiped in _degraded.Keys)
                        _cleared.Add(wiped);

                    _degraded.Clear();
                    return;
                }

                bool broke = type == LeafLifecycleEvents.Degraded;
                bool fixedIt = type == LeafLifecycleEvents.Recovered;

                if (!broke && !fixedIt)
                    return;

                if (!root.TryGetProperty("Data", out JsonElement data)
                    || data.ValueKind != JsonValueKind.Object
                    || !data.TryGetProperty(LeafLifecycleFields.Component, out JsonElement component)
                    || component.ValueKind != JsonValueKind.String
                    || component.GetString() is not { Length: > 0 } name)
                {
                    return;
                }

                _recovered.Remove(name);
                _cleared.Remove(name);

                if (broke)
                {
                    _degraded[name] = new LeafDegradation(name, DetailOf(data), TimestampOf(root));
                }
                else
                {
                    _degraded.Remove(name);
                    _recovered.Add(name);
                }
            }
            catch (JsonException)
            {
                // A malformed line says nothing about this producer's state.
            }
        }

        private static string? DetailOf(JsonElement data) =>
            data.TryGetProperty(LeafLifecycleFields.Detail, out JsonElement detail)
            && detail.ValueKind == JsonValueKind.String
                ? detail.GetString()
                : null;

        // Absent or unparseable is unknown, never a stand-in moment: when a fault began is only
        // knowable from the line that reported it.
        private static DateTimeOffset? TimestampOf(JsonElement root) =>
            root.TryGetProperty("Timestamp", out JsonElement ts)
            && ts.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                ts.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at)
                ? at
                : null;
    }
}

/// <summary>
/// One component a producer reports broken, and what it said about it.
/// </summary>
/// <param name="Component">The component id the leaf named.</param>
/// <param name="Detail">What the leaf said is wrong, or null when it said nothing.</param>
/// <param name="Since">When the fault was reported, or null when the line carried no readable timestamp.</param>
public sealed record LeafDegradation(string Component, string? Detail, DateTimeOffset? Since);

/// <summary>
/// What one replay of a producer's newest journal segment found.
/// </summary>
/// <param name="Degraded">The components reported broken and not since reported fixed.</param>
/// <param name="Recovered">The components whose last transition is a reported recovery.</param>
/// <param name="Cleared">
/// The components reported broken before the leaf last came up, and not mentioned since. Not a recovery:
/// the fresh process has not re-observed them, in either direction.
/// </param>
public sealed record LeafStateReport(
    IReadOnlyList<LeafDegradation> Degraded,
    IReadOnlyCollection<string> Recovered,
    IReadOnlyCollection<string> Cleared)
{
    /// <summary>A report of nothing: no journal, or one that could not be read.</summary>
    public static LeafStateReport Empty { get; } = new([], [], []);
}
