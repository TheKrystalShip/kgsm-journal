using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Where a consumer keeps one journal position <em>per producer</em>.
/// </summary>
/// <remarks>
/// <para>
/// A position is a segment plus a byte offset, and both are meaningful only within one journal —
/// two producers collide on an offset constantly. So a consumer reading N journals holds N cursors
/// and advances each independently: a leaf that was down catches up from where that leaf's journal
/// left off, without replaying or skipping any other's.
/// </para>
/// <para>
/// The same at-least-once rule as <see cref="IEventCursorStore"/> applies to each: a cursor is
/// stored only past events already dispatched, so a crash costs re-delivery and never loss, and a
/// consumer that persists what it reads must be idempotent per event id.
/// </para>
/// </remarks>
public interface IFederatedEventCursorStore
{
    /// <summary>
    /// Reads the stored position for one producer's journal, or <see langword="null"/> when none has
    /// been recorded.
    /// </summary>
    /// <param name="producer">The producer whose journal the cursor is for.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The stored cursor, or <see langword="null"/>.</returns>
    ValueTask<EventCursor?> LoadAsync(string producer, CancellationToken token = default);

    /// <summary>
    /// Records the position to resume one producer's journal from.
    /// </summary>
    /// <param name="producer">The producer whose journal the cursor is for.</param>
    /// <param name="cursor">The position to store.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task that completes when the cursor has been stored.</returns>
    ValueTask SaveAsync(string producer, EventCursor cursor, CancellationToken token = default);
}
