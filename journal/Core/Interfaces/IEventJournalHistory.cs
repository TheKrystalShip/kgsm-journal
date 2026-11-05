using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Queries the engine's event journal — the durable record of what KGSM did.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <see cref="IEventJournalReader"/>: that one tails the journal for events as
/// they happen, this one reads back over what it already holds. Both read the same files, and
/// neither needs anything running besides the engine that wrote them — history is a property of
/// the record on disk, not of any daemon being up.
/// </para>
/// <para>
/// There is no index and no cache. The journal is segmented by date, so a bounded window opens
/// only the segments that can contain it, and a scan stops as soon as the page is full. What
/// that buys is the absence of a second copy to keep in step: nothing here can disagree with
/// the record, go stale, or need rebuilding.
/// </para>
/// <para>
/// Implementations never throw for a missing or unreadable journal — that is reported as
/// <see cref="EventHistoryPage.JournalReadable"/> being false, so a consumer's failure path is
/// data rather than an exception.
/// </para>
/// </remarks>
public interface IEventJournalHistory
{
    /// <summary>
    /// Reads one page of events, newest first.
    /// </summary>
    /// <param name="query">The window, filters and page size. Filters are ANDed; unset places no constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching events and the honesty signals that qualify them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    Task<EventHistoryPage> QueryAsync(EventHistoryQuery query, CancellationToken cancellationToken = default);
}
