using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// A cursor store that remembers nothing. Every run starts from the consumer's cold-start
/// position, which is the right behaviour for a consumer whose interest in an event expires
/// with the process — a cache invalidator, a live feed — and for which replaying yesterday
/// would be noise rather than history.
/// </summary>
public sealed class NullEventCursorStore : IEventCursorStore
{
    /// <inheritdoc/>
    public ValueTask<EventCursor?> LoadAsync(CancellationToken token = default)
        => ValueTask.FromResult<EventCursor?>(null);

    /// <inheritdoc/>
    public ValueTask SaveAsync(EventCursor cursor, CancellationToken token = default)
        => ValueTask.CompletedTask;
}
