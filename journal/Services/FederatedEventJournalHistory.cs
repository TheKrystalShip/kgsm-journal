using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Reads history across every producer's journal and merges the result into one page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregation belongs here, not in a consumer.</b> The journal answers for itself, and a
/// consumer holding the only merged view would make every other consumer depend on that consumer
/// being installed. Because this sits in the library each surface aggregates natively, and the one
/// that serves the merge over HTTP is exposing it rather than being it.
/// </para>
/// <para>
/// <b>A journal that is not in the source list is not read</b>, and a producer that is installed but
/// unreadable is reported as such per-journal (<see cref="JournalCoverage.Readable"/>) rather than
/// silently contributing nothing. The page's own <see cref="EventHistoryPage.JournalReadable"/> is
/// true when <em>any</em> journal answered, so an absent leaf degrades the page instead of emptying
/// it.
/// </para>
/// <para>
/// <b>Ordering</b> is timestamp-descending with the event id as the tie-break, exactly as a single
/// journal orders itself. Ids carry the producer, so at one instant they sort by
/// <c>(producer, segment, offset)</c> — deterministic rather than true, because no host-local
/// mechanism can order two independent appends inside the same millisecond. Every reader sorting the
/// same way is what matters; which of two simultaneous events comes first does not.
/// </para>
/// <para>
/// <b>The scan budget applies per journal, not per query.</b> Each journal answers to the same
/// depth whatever the fleet size, so a page's completeness does not quietly degrade as leaves are
/// added — at the cost of total work scaling with the number of producers.
/// </para>
/// </remarks>
public sealed class FederatedEventJournalHistory : IEventJournalHistory
{
    private readonly List<(string Producer, EventJournalHistory Reader)> _readers;
    private readonly ILogger<FederatedEventJournalHistory> _logger;

    /// <summary>
    /// Initializes a reader over the given journals.
    /// </summary>
    /// <param name="sources">
    /// The journals to merge. An empty list is valid and answers every query as unreadable — a host
    /// with no discovered journals has no history, which is a different statement from an error.
    /// </param>
    /// <param name="budgetBytes">The scan budget allowed for each journal.</param>
    /// <param name="loggerFactory">Factory for the per-journal readers' loggers.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a source names an invalid producer, a blank directory, or a producer that
    /// appears twice — two journals claiming one producer would make the ids derived from them
    /// collide, which is the one thing the producer prefix exists to prevent.
    /// </exception>
    public FederatedEventJournalHistory(
        IReadOnlyList<JournalSource> sources,
        long budgetBytes,
        ILoggerFactory loggerFactory,
        ILogger<FederatedEventJournalHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        ArgumentNullException.ThrowIfNull(loggerFactory, nameof(loggerFactory));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        _logger = logger;
        _readers = new List<(string, EventJournalHistory)>(sources.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        ILogger<EventJournalHistory> readerLogger = loggerFactory.CreateLogger<EventJournalHistory>();

        foreach (JournalSource source in sources)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(sources));
            JournalProducer.Validate(source.Producer, nameof(sources));

            if (!seen.Add(source.Producer))
                throw new ArgumentException($"Producer '{source.Producer}' is named by more than one journal source.", nameof(sources));

            _readers.Add((
                source.Producer,
                new EventJournalHistory(source.Producer, source.Directory, budgetBytes, readerLogger)));
        }
    }

    /// <inheritdoc/>
    public async Task<EventHistoryPage> QueryAsync(
        EventHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));

        if (_readers.Count == 0)
        {
            _logger.LogDebug("No journals configured; history is unreadable");
            return EventHistoryPage.Unreadable;
        }

        int limit = query.Limit <= 0
            ? EventHistoryQuery.DefaultLimit
            : Math.Min(query.Limit, EventHistoryQuery.MaxLimit);

        // Every journal is asked for a full page: the merge keeps the newest `limit` overall, and any
        // one journal could supply all of them.
        EventHistoryQuery perJournal = query with { Limit = limit };

        var merged = new List<EventHistoryEntry>(limit * _readers.Count);
        var coverage = new List<JournalCoverage>(_readers.Count);

        foreach ((string producer, EventJournalHistory reader) in _readers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventHistoryPage page = await reader
                .QueryAsync(perJournal, cancellationToken)
                .ConfigureAwait(false);

            merged.AddRange(page.Events);
            coverage.Add(new JournalCoverage(
                producer, page.CoverageFrom, page.JournalReadable, page.Truncated));

            if (!page.JournalReadable)
                _logger.LogDebug("Journal for producer {Producer} is absent or unreadable", producer);
        }

        merged.Sort(static (a, b) =>
        {
            int byTime = b.Ts.CompareTo(a.Ts);
            return byTime != 0 ? byTime : string.CompareOrdinal(b.Id, a.Id);
        });

        if (merged.Count > limit)
            merged.RemoveRange(limit, merged.Count - limit);

        long? nextTs = null;
        string? nextId = null;
        if (merged.Count == limit && merged.Count > 0)
        {
            nextTs = merged[^1].Ts.ToUnixTimeMilliseconds();
            nextId = merged[^1].Id;
        }

        return new EventHistoryPage(
            merged,
            nextTs,
            nextId,
            CollapseCoverage(coverage),
            coverage.Exists(static c => c.Truncated),
            coverage.Exists(static c => c.Readable),
            coverage);
    }

    /// <summary>
    /// The oldest moment the <em>merged</em> history can answer for completely: the newest of the
    /// readable journals' floors.
    /// </summary>
    /// <remarks>
    /// Deliberately the newest and not the oldest. Past that point at least one producer's retention
    /// has already dropped what it held, so a window reaching earlier is answered by some journals
    /// and not others — reporting the oldest floor would present that as full coverage. A readable
    /// journal holding no events constrains nothing: it has lost nothing.
    /// </remarks>
    private static DateTimeOffset? CollapseCoverage(List<JournalCoverage> coverage)
    {
        DateTimeOffset? newest = null;

        foreach (JournalCoverage c in coverage)
        {
            if (!c.Readable || c.CoverageFrom is not { } from)
                continue;

            if (newest is null || from > newest)
                newest = from;
        }

        return newest;
    }
}
