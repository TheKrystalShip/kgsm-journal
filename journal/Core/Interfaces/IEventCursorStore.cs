using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Where a consumer keeps its journal position across restarts. Storage is the consumer's
/// own business: a consumer that already owns a database should store the cursor there,
/// alongside whatever it derives from the events, so the two cannot drift apart.
/// </summary>
/// <remarks>
/// The reader saves a cursor only for lines it has already handed to handlers, which makes
/// delivery <b>at-least-once</b>: a crash between dispatch and save replays the tail of the
/// stream on restart. Consumers that persist what they read must therefore be idempotent —
/// deriving a deterministic identity per event (as <see cref="Events.AuditId"/> does) is
/// what makes the replay harmless.
/// </remarks>
public interface IEventCursorStore
{
    /// <summary>
    /// Reads the stored position, or <see langword="null"/> if this consumer has never
    /// recorded one.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The stored cursor, or <see langword="null"/>.</returns>
    ValueTask<EventCursor?> LoadAsync(CancellationToken token = default);

    /// <summary>
    /// Records the position to resume from. Called after the events before it have been
    /// dispatched, so a store that fails to persist costs re-delivery, never loss.
    /// </summary>
    /// <param name="cursor">The position to store.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>A task that completes when the cursor has been stored.</returns>
    ValueTask SaveAsync(EventCursor cursor, CancellationToken token = default);
}
