using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Keeps no position for any producer, so every journal starts from its cold-start position on every
/// run.
/// </summary>
/// <remarks>
/// The federated counterpart to <see cref="NullEventCursorStore"/>, and a deliberate choice rather
/// than a degraded one. A consumer that derives no durable state from the events it reads — one that
/// shapes each into a live view and announces it onward — must <em>not</em> resume: replaying a
/// backlog would re-announce things that were announced when they happened. Such a consumer wants no
/// cursors at all, and saying so explicitly is clearer than pointing a real store at a path it hopes
/// nothing reads.
/// </remarks>
public sealed class NullFederatedEventCursorStore : IFederatedEventCursorStore
{
    /// <inheritdoc/>
    public ValueTask<EventCursor?> LoadAsync(string producer, CancellationToken token = default)
        => ValueTask.FromResult<EventCursor?>(null);

    /// <inheritdoc/>
    public ValueTask SaveAsync(string producer, EventCursor cursor, CancellationToken token = default)
        => ValueTask.CompletedTask;
}
