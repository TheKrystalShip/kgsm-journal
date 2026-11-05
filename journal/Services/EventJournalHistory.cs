using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Answers history queries by reading the engine's journal segments directly.
/// </summary>
/// <remarks>
/// <para>
/// Three properties of the journal make a per-query scan cheap enough that an index would be
/// optimizing a problem that does not exist. Segments are named by date, so a bounded window
/// is narrowed to a handful of files by their <em>names</em>, before one is opened. Segments
/// are visited newest-first and the scan stops the moment the page is full, so a typical
/// "recent events" read touches one file. And each segment is streamed forward once, with
/// matches kept in a ring buffer the size of the page — which by construction holds that
/// segment's newest matches — so memory is bounded by the page size rather than the segment,
/// and no file is ever read backwards or loaded whole.
/// </para>
/// <para>
/// The scan is bounded by <see cref="JournalReadOptions.EventHistoryScanBudgetBytes"/>. Hitting it
/// ends the read and sets <see cref="EventHistoryPage.Truncated"/>, so a bounded answer is
/// never handed back as an exhaustive one.
/// </para>
/// </remarks>
public sealed class EventJournalHistory : IEventJournalHistory
{
    private const string SegmentPattern = "*.ndjson";
    private const int ReadBufferSize = 64 * 1024;

    /// <summary>
    /// How far either side of a query's window the candidate segments reach.
    /// <para>
    /// The engine names a segment from the clock when an emit begins, so an emit that straddles
    /// midnight lands in the previous day's file. A segment can therefore hold an event dated
    /// just outside the day its name claims, and pruning candidates on the name alone would
    /// skip it. One day of slack covers that by orders of magnitude, and costs at most two extra
    /// files.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SegmentSlack = TimeSpan.FromDays(1);

    /// <summary>
    /// The positions already described as unreadable, so a standing fault in a segment is reported
    /// once rather than on every scan that walks past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A segment is never rewritten, so a line that cannot be read now cannot be read later either:
    /// every query reaching back that far meets the same position again, and at a surface's polling
    /// cadence that is tens of thousands of identical stack traces a day burying everything else in
    /// the log. The first encounter carries the exception, the rest go to Debug — so the fault is
    /// still described in full, and still visible for as long as it stands.
    /// </para>
    /// <para>
    /// Process-wide because the reader is resolved per scope: a memory held on the instance would
    /// forget between one query and the next, which is the same thing as having none.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<string, byte> ReportedFaults = new(StringComparer.Ordinal);

    /// <summary>
    /// How many distinct faulted positions are remembered. A journal holds a handful at most; past
    /// this many, something is wrong with the directory itself rather than with a line, and repeating
    /// the reports is the better failure than growing without bound.
    /// </summary>
    private const int MaxReportedFaults = 512;

    /// <summary>Whether this is the first time <paramref name="offset"/> in <paramref name="path"/> has been reported.</summary>
    private static bool FirstReportOf(string path, long offset) =>
        ReportedFaults.Count >= MaxReportedFaults
        || ReportedFaults.TryAdd(
            string.Create(CultureInfo.InvariantCulture, $"{path}+{offset}"), 0);

    private readonly string _directory;
    private readonly long _budgetBytes;
    private readonly string? _producer;
    private readonly bool _prefixIds;
    private readonly ILogger<EventJournalHistory> _logger;

    /// <summary>
    /// Initializes a new reader over the journal directory named by <paramref name="options"/>.
    /// </summary>
    /// <param name="options">KGSM options supplying the journal directory and the scan budget.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no journal directory.</exception>
    public EventJournalHistory(JournalReadOptions options, ILogger<EventJournalHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        if (string.IsNullOrWhiteSpace(options.EventJournalDirectory))
            throw new ArgumentException("Event journal directory cannot be null, empty, or whitespace.", nameof(options));

        _directory = options.EventJournalDirectory;
        _budgetBytes = options.EventHistoryScanBudgetBytes;
        _producer = null;
        _prefixIds = false;
        _logger = logger;
    }

    /// <summary>
    /// Initializes a reader over one named producer's journal, for a caller merging several.
    /// </summary>
    /// <remarks>
    /// The ids this reader produces carry the producer
    /// (<see cref="AuditId.ForPosition(string, string, long)"/>), which is what keeps them unique
    /// once merged — a byte offset only identifies an event within one journal, and two producers
    /// collide on it constantly. That makes the ids from this constructor different values from the
    /// ones the options constructor produces for the same event, so a reader picks one and stays
    /// with it.
    /// </remarks>
    /// <param name="producer">The producer id whose journal <paramref name="directory"/> holds.</param>
    /// <param name="directory">The journal directory.</param>
    /// <param name="budgetBytes">The scan budget for one query.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="producer"/> is not a valid producer id, or
    /// <paramref name="directory"/> is blank.
    /// </exception>
    public EventJournalHistory(
        string producer, string directory, long budgetBytes, ILogger<EventJournalHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        JournalProducer.Validate(producer, nameof(producer));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory, nameof(directory));

        _directory = directory;
        _budgetBytes = budgetBytes > 0 ? budgetBytes : JournalReadOptions.DefaultEventHistoryScanBudgetBytes;
        _producer = producer;
        _prefixIds = true;
        _logger = logger;
    }

    /// <summary>The producer whose journal this reads, or null when it was not told.</summary>
    public string? Producer => _producer;

    /// <inheritdoc/>
    public async Task<EventHistoryPage> QueryAsync(EventHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));

        IReadOnlyList<string> segments = ListSegments();
        if (segments.Count == 0)
        {
            // A directory that is there and holds nothing is a producer that has recorded nothing yet —
            // readable, with no coverage. That is a different answer from a journal this reader cannot
            // see at all, and the two stopped being interchangeable once a producer creates its journal
            // directory up front so readers can discover it before its first event.
            if (Directory.Exists(_directory))
            {
                _logger.LogDebug("Event journal at {Directory} holds no events yet", _directory);
                return EventHistoryPage.Empty(coverageFrom: null);
            }

            _logger.LogDebug("No event journal at {Directory}", _directory);
            return EventHistoryPage.Unreadable;
        }

        int limit = query.Limit <= 0
            ? EventHistoryQuery.DefaultLimit
            : Math.Min(query.Limit, EventHistoryQuery.MaxLimit);

        DateTimeOffset? coverageFrom = await ReadCoverageFromAsync(segments[0], cancellationToken).ConfigureAwait(false);

        var results = new List<EventHistoryEntry>(limit);
        long scanned = 0;
        bool truncated = false;

        foreach (string segment in CandidateSegments(segments, query))
        {
            if (results.Count >= limit)
                break;

            (List<EventHistoryEntry> matches, long bytes, bool budgetHit) = await ScanSegmentAsync(
                segment, query, limit - results.Count, _budgetBytes - scanned, cancellationToken)
                .ConfigureAwait(false);

            scanned += bytes;
            results.AddRange(matches);

            if (budgetHit)
            {
                truncated = true;
                _logger.LogWarning(
                    "Event history scan stopped at its {Budget}-byte budget while reading {Segment}; the page is a prefix of the answer",
                    _budgetBytes, segment);
                break;
            }
        }

        // Segments are visited newest-first and each contributes its own newest-first block, so
        // the concatenation is already in order under the normal case where file order and
        // timestamp order agree. A final sort makes that independent of the assumption, at the
        // cost of ordering one page.
        results.Sort(static (a, b) => PageOrderAscending.Compare(b, a));

        if (results.Count > limit)
            results.RemoveRange(limit, results.Count - limit);

        // A full page might have more behind it; a partial one is the end of the road, so its
        // cursor is honestly null rather than pointing at an empty result.
        long? nextTs = null;
        string? nextId = null;
        if (results.Count == limit && results.Count > 0)
        {
            nextTs = results[^1].Ts.ToUnixTimeMilliseconds();
            nextId = results[^1].Id;
        }

        return new EventHistoryPage(results, nextTs, nextId, coverageFrom, truncated, true);
    }

    /// <summary>
    /// The page's order, ascending — oldest and lowest-id first.
    /// </summary>
    /// <remarks>
    /// <b>One definition, used twice.</b> The per-segment scan evicts the smallest under this to keep
    /// the top of a segment, and the assembled page is sorted by its reverse. Two copies of the rule
    /// would be free to drift, and a scan selecting by one order while the page is sorted by another
    /// drops rows that belong on it — silently, because what is dropped is never counted.
    /// </remarks>
    private static readonly IComparer<EventHistoryEntry> PageOrderAscending =
        Comparer<EventHistoryEntry>.Create(static (a, b) =>
        {
            int byTime = a.Ts.CompareTo(b.Ts);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
        });

    /// <summary>
    /// The segments that can contain events matching the query, newest first.
    /// </summary>
    /// <remarks>
    /// Names are dates, so the window bounds prune the list without opening anything. The bounds
    /// are widened by <see cref="SegmentSlack"/> and the cursor's own segment is always kept:
    /// this is an optimization over a filter that is applied per event regardless, so it is
    /// deliberately generous — including a segment that turns out to hold nothing costs one file
    /// read, while excluding one that did costs a missing event.
    /// </remarks>
    private static IEnumerable<string> CandidateSegments(
        IReadOnlyList<string> segments, EventHistoryQuery query)
    {
        DateOnly? from = query.SinceMs is { } since
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime - SegmentSlack)
            : null;

        DateOnly? to = query.UntilMs is { } until
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(until).UtcDateTime + SegmentSlack)
            : null;

        // Nothing after the cursor's timestamp can qualify, so newer segments are skipped — with the
        // same slack, since a straggler write can date an event slightly outside the segment holding it.
        DateOnly? cursorCeiling = query.BeforeTsMs is { } cursorMs
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(cursorMs).UtcDateTime + SegmentSlack)
            : null;

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            string segment = segments[i];

            // A segment whose name is not a date is outside the engine's own convention; it is
            // always scanned rather than guessed about.
            if (!DateOnly.TryParse(Path.GetFileNameWithoutExtension(segment), out DateOnly date))
            {
                yield return segment;
                continue;
            }

            if (from is { } lower && date < lower) continue;
            if (to is { } upper && date > upper) continue;
            if (cursorCeiling is { } ceiling && date > ceiling) continue;

            yield return segment;
        }
    }

    /// <summary>
    /// Streams one segment forward, keeping the newest <paramref name="wanted"/> matches.
    /// </summary>
    /// <returns>
    /// The matches newest-first, how many bytes were read, and whether the budget ran out
    /// mid-segment.
    /// </returns>
    private async Task<(List<EventHistoryEntry> Matches, long Bytes, bool BudgetHit)> ScanSegmentAsync(
        string segment, EventHistoryQuery query, int wanted, long budget, CancellationToken token)
    {
        var matches = new List<EventHistoryEntry>();
        string path = Path.Combine(_directory, segment);
        string stem = Path.GetFileNameWithoutExtension(segment);

        if (budget <= 0)
            return (matches, 0, true);

        FileStream stream;
        try
        {
            // The engine appends to this file while it is open and retention may unlink it
            // underneath the read; neither is an error.
            stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, ReadBufferSize, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            return (matches, 0, false);
        }
        catch (DirectoryNotFoundException)
        {
            return (matches, 0, false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Not permitted to read the event journal segment {Segment}", segment);
            return (matches, 0, false);
        }

        // Bounded top-K by the SAME order the page is sorted in, not by file order.
        //
        // Dropping the oldest line as it streams past is only right while id order and file order
        // agree, which held while every id was derived from a byte offset. A line's own id does not
        // sort that way: within one millisecond a named line can rank BELOW an unnamed one written
        // after it, and a plain ring would already have discarded the one that outranks it — a row
        // the final sort never sees and no page ever serves. Measured as a silent skip in a four-row
        // window paged one row at a time.
        //
        // A heap of the same size keeps that O(1)-memory single forward pass and costs a comparison
        // per match. The smallest by (timestamp, id) is what gets evicted, so what survives is the
        // segment's top `wanted` under the page's own comparator whatever order the file is in —
        // which also makes the read correct for a segment whose lines are not in timestamp order,
        // something the final sort was documented as covering and could not.
        var ring = new PriorityQueue<EventHistoryEntry, EventHistoryEntry>(wanted, PageOrderAscending);
        long bytes = 0;
        bool budgetHit = false;

        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, ReadBufferSize);

            long offset = 0;
            string? line;

            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) is not null)
            {
                // The offset of the NEXT line, tracked as the encoded length plus the newline.
                // Every event is one whole line and the engine never rewrites a segment, so this
                // stays exact for the lifetime of the id it produces.
                long lineStart = offset;
                offset += Encoding.UTF8.GetByteCount(line) + 1;

                bytes = offset;
                if (bytes > budget)
                {
                    budgetHit = true;
                    break;
                }

                // Read past the hole an unclean shutdown left, if there is one. The offset above is
                // taken from the line as it sits on disk, so an id keeps naming the same bytes
                // whether or not the line it names had zeros in front of it.
                line = JournalLine.WithoutHole(line);

                if (line.Length == 0)
                    continue;

                if (!CouldMatch(line, query))
                    continue;

                EventWrapper? wrapper = Deserialize(line, path, lineStart);
                if (wrapper is null)
                    continue;

                if (wrapper.Timestamp is not { } ts)
                {
                    // An event with no timestamp cannot be placed in a time-ordered history, and
                    // inventing one would put a fabricated moment in the audit trail. It is
                    // reported and skipped.
                    if (FirstReportOf(path, lineStart))
                        _logger.LogWarning(
                            "Event journal {Segment}+{Offset} carries no Timestamp and is absent from history",
                            segment, lineStart);
                    else
                        _logger.LogDebug(
                            "Event journal {Segment}+{Offset} carries no Timestamp and is absent from history",
                            segment, lineStart);

                    continue;
                }

                // The line's own name when it has one, the position when it does not — and the SAME
                // choice the live path makes, or one event would come back with two ids depending on
                // which side served it.
                string id = _prefixIds && _producer is { } p
                    ? AuditId.ForLine(wrapper.Id, p, stem, lineStart)
                    : AuditId.ForLine(wrapper.Id, stem, lineStart);

                if (!Matches(wrapper, ts, id, query))
                    continue;

                var entry = new EventHistoryEntry(
                    id, ts, wrapper.EventType,
                    InstanceOf(wrapper.Data),
                    BlueprintOf(wrapper.Data),
                    wrapper.Actor, wrapper.Origin, wrapper.Hostname,
                    wrapper.Data,
                    // Stamped from the journal this reader was pointed at, never from the line.
                    _producer,
                    wrapper.OpId, wrapper.RunId, wrapper.During,
                    // What the producer said about its own event. Carried through untouched — a
                    // reader that substituted a default here would be putting its own judgement on
                    // the line under the producer's name.
                    wrapper.Severity, wrapper.Outcome, wrapper.Summary);

                // Its own priority: the comparator reads the fields, so there is nothing to keep in
                // step between the two arguments.
                ring.Enqueue(entry, entry);

                if (ring.Count > wanted)
                    ring.Dequeue();
            }
        }

        // Drained in heap order, which is not the page's order — the caller sorts every segment's
        // contribution together anyway, so ordering here would be work done twice.
        matches.AddRange(ring.UnorderedItems.Select(static item => item.Element));
        return (matches, bytes, budgetHit);
    }

    /// <summary>
    /// A cheap reject for a line that cannot possibly match, before the cost of parsing it.
    /// </summary>
    /// <remarks>
    /// Substring containment only, never a shape assumption: a filter's value appearing nowhere
    /// in the line means no parse of it can match, whatever the formatting. The reverse is not
    /// claimed — a line that passes here is still checked properly after parsing — so this can
    /// only ever save work, never change an answer.
    /// </remarks>
    private static bool CouldMatch(string line, EventHistoryQuery query)
    {
        if (!string.IsNullOrEmpty(query.Type) && !line.Contains(query.Type, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(query.Instance) && !line.Contains(query.Instance, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(query.Blueprint) && !line.Contains(query.Blueprint, StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>The authoritative filter, applied to a parsed envelope.</summary>
    private static bool Matches(
        EventWrapper wrapper, DateTimeOffset ts, string id, EventHistoryQuery query)
    {
        if (!string.IsNullOrEmpty(query.Type)
            && !string.Equals(wrapper.EventType, query.Type, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(query.Instance)
            && !string.Equals(InstanceOf(wrapper.Data), query.Instance, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(query.Blueprint)
            && !string.Equals(BlueprintOf(wrapper.Data), query.Blueprint, StringComparison.Ordinal))
            return false;

        long tsMs = ts.ToUnixTimeMilliseconds();
        if (query.SinceMs is { } since && tsMs < since) return false;
        if (query.UntilMs is { } until && tsMs > until) return false;

        // The same composite predicate the ordering uses: strictly older, or the same instant and a
        // lower id. Matching the sort exactly is what stops paging from repeating or skipping an event
        // where several share a timestamp. The id may name a row from another source entirely — a
        // caller merging two feeds pages both from one cursor — so it is only ever compared, never
        // resolved.
        if (query.BeforeTsMs is { } cursorMs)
        {
            if (tsMs > cursorMs) return false;
            if (tsMs == cursorMs && query.BeforeId is { } cursorId
                && string.CompareOrdinal(id, cursorId) >= 0) return false;
        }

        return true;
    }

    /// <summary>
    /// The timestamp of the oldest event the journal still holds — what the history can answer
    /// for. Read from the first line of the oldest segment, because retention deletes whole
    /// segments oldest-first and never truncates one.
    /// </summary>
    private async Task<DateTimeOffset?> ReadCoverageFromAsync(string oldest, CancellationToken token)
    {
        string path = Path.Combine(_directory, oldest);

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, ReadBufferSize, useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) is not null)
            {
                line = JournalLine.WithoutHole(line);

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                return Deserialize(line, path, 0)?.Timestamp;
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not read the oldest event journal segment {Segment}", oldest);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Not permitted to read the oldest event journal segment {Segment}", oldest);
        }

        return null;
    }

    /// <summary>
    /// Deserializes one journal line, or null when it cannot be read. A malformed line is
    /// reported and skipped — one bad line never fails a whole query, and never repeats its
    /// report on every query either (<see cref="ReportedFaults"/>).
    /// </summary>
    private EventWrapper? Deserialize(string line, string path, long offset)
    {
        try
        {
            EventWrapper? wrapper = JsonSerializer.Deserialize(line, JournalJsonContext.Default.EventWrapper);

                // A journal holds what earlier builds wrote for as long as retention keeps it, so a
                // line can carry a name that has since been renamed. Resolved here, once, so every
                // consumer above works in the current vocabulary and none of them carries a second
                // name for anything.
            if (wrapper is not null)
                wrapper.EventType = LegacyEventNames.Canonical(wrapper.EventType);
            if (wrapper is null || string.IsNullOrEmpty(wrapper.EventType))
            {
                if (FirstReportOf(path, offset))
                    _logger.LogWarning("Event journal {Path}+{Offset} holds no readable event", path, offset);
                else
                    _logger.LogDebug("Event journal {Path}+{Offset} holds no readable event", path, offset);

                return null;
            }

            return wrapper;
        }
        catch (JsonException ex)
        {
            if (FirstReportOf(path, offset))
                _logger.LogWarning(ex, "Event journal {Path}+{Offset} is not valid JSON", path, offset);
            else
                _logger.LogDebug("Event journal {Path}+{Offset} is not valid JSON", path, offset);

            return null;
        }
    }

    /// <summary>The instance a payload names, or null when it carries none.</summary>
    private static string? InstanceOf(KgsmEventDataBase? data) => NonEmpty(data switch
    {
        EventDataBase instance => instance.InstanceName,
        UnrecognizedEventData unrecognized => unrecognized.InstanceName,
        _ => null,
    });

    /// <summary>The blueprint a payload names, or null when it carries none.</summary>
    private static string? BlueprintOf(KgsmEventDataBase? data) => NonEmpty(data switch
    {
        BlueprintEventDataBase blueprint => blueprint.BlueprintName,
        UnrecognizedEventData unrecognized => unrecognized.BlueprintName,
        _ => null,
    });

    private static string? NonEmpty(string? name) => string.IsNullOrEmpty(name) ? null : name;

    /// <summary>
    /// The journal's segment file names, oldest first. Names are dates, so ordinal sort order is
    /// chronological order.
    /// </summary>
    private IReadOnlyList<string> ListSegments()
    {
        if (!Directory.Exists(_directory))
            return [];

        try
        {
            string[] paths = Directory.GetFiles(_directory, SegmentPattern);
            string[] names = new string[paths.Length];

            for (int i = 0; i < paths.Length; i++)
                names[i] = Path.GetFileName(paths[i]);

            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to list event journal segments in {Directory}", _directory);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Not permitted to read the event journal directory {Directory}", _directory);
            return [];
        }
    }
}
