using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Tails every producer's journal at once and delivers all of them as one stream.
/// </summary>
/// <remarks>
/// <para>
/// The live counterpart to <see cref="FederatedEventJournalHistory"/>. It composes one
/// <see cref="EventJournalReader"/> per producer rather than reimplementing the tail: whole-line
/// framing, segment rolling, straggler grace and gap detection are already solved once, and a second
/// implementation of them would be a second set of bugs.
/// </para>
/// <para>
/// <b>Each journal advances independently.</b> Every reader holds its own cursor, scoped to its
/// producer, so a leaf that was down catches up from where <em>its</em> journal left off without
/// replaying or skipping any other's. There is deliberately no attempt to order events across
/// journals as they arrive: a live stream delivers each line as it lands, and the total order is a
/// property of a <em>read</em> over the record, which is what the history reader is for.
/// </para>
/// <para>
/// <b>Every event carries its producer.</b> The position handed to a consumer names the journal the
/// line came from, stamped here from the reader that produced it — never taken from the line — so a
/// consumer can key an event by <see cref="AuditId.ForPosition(string, string, long)"/> and never
/// collide two producers that happen to share a byte offset.
/// </para>
/// <para>
/// A journal whose directory does not exist yet is not an error: the underlying reader tolerates an
/// absent directory and picks up the first segment when it appears, so a leaf installed later starts
/// being tailed without a restart.
/// </para>
/// </remarks>
public sealed class FederatedEventSource : IEventSource
{
    private readonly List<(string Producer, EventJournalReader Reader)> _readers;
    private readonly ILogger<FederatedEventSource> _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public event Func<string, EventPosition, Task>? EventReceived;

    /// <summary>
    /// Raised when one producer's stored position could not be honoured, naming which producer.
    /// </summary>
    /// <remarks>
    /// A consumer that persists history should record this per producer, so it can say that its
    /// record of <em>that</em> journal is incomplete before that point rather than implying coverage
    /// of all of them.
    /// </remarks>
    public event Func<string, EventJournalGap, Task>? GapDetected;

    /// <summary>
    /// Initializes a source over the given journals.
    /// </summary>
    /// <param name="sources">The journals to tail. Empty is valid and delivers nothing.</param>
    /// <param name="cursors">Where each producer's position is stored between runs.</param>
    /// <param name="startPosition">Where each journal begins when it has no stored position.</param>
    /// <param name="loggerFactory">Factory for the per-journal readers' loggers.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a source names an invalid producer, a blank directory, or a producer that appears
    /// twice — two journals claiming one producer would collide on the ids derived from them.
    /// </exception>
    public FederatedEventSource(
        IReadOnlyList<JournalSource> sources,
        IFederatedEventCursorStore cursors,
        EventStartPosition startPosition,
        ILoggerFactory loggerFactory,
        ILogger<FederatedEventSource> logger)
    {
        ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        ArgumentNullException.ThrowIfNull(cursors, nameof(cursors));
        ArgumentNullException.ThrowIfNull(loggerFactory, nameof(loggerFactory));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        _logger = logger;
        _readers = new List<(string, EventJournalReader)>(sources.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        ILogger<EventJournalReader> readerLogger = loggerFactory.CreateLogger<EventJournalReader>();

        foreach (JournalSource source in sources)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(sources));
            JournalProducer.Validate(source.Producer, nameof(sources));
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Directory, nameof(sources));

            if (!seen.Add(source.Producer))
                throw new ArgumentException($"Producer '{source.Producer}' is named by more than one journal source.", nameof(sources));

            var reader = new EventJournalReader(
                new JournalReadOptions
                {
                    EventJournalDirectory = source.Directory,
                    EventStartPosition = startPosition,
                },
                new ProducerScopedCursorStore(cursors, source.Producer),
                readerLogger);

            string producer = source.Producer;

            // Stamp the producer onto every position as it leaves the reader. The reader knows only
            // its directory; which producer that directory belongs to is this class's knowledge, and
            // attaching it here is what keeps it off the wire and out of the line.
            reader.EventReceived += (json, position) =>
                EventReceived?.Invoke(json, position with { Producer = producer }) ?? Task.CompletedTask;

            reader.GapDetected += gap =>
                GapDetected?.Invoke(producer, gap) ?? Task.CompletedTask;

            _readers.Add((producer, reader));
        }
    }

    /// <summary>The producers this source tails, in the order they were configured.</summary>
    public IReadOnlyList<string> Producers => [.. _readers.Select(static r => r.Producer)];

    /// <inheritdoc/>
    public async Task StartListeningAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(FederatedEventSource));

        if (_readers.Count == 0)
        {
            _logger.LogWarning("No journals configured; no events will be delivered");
            return;
        }

        _logger.LogInformation(
            "Tailing {Count} event journals: {Producers}",
            _readers.Count, string.Join(", ", Producers));

        // One tail per journal, run together. Task.WhenAll surfaces the first fault after all have
        // settled, so one journal failing does not silently take the others down with it — each
        // reader already swallows a bad line, so reaching here means the tail itself stopped.
        await Task.WhenAll(_readers.Select(r => r.Reader.StartListeningAsync(token))).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach ((_, EventJournalReader reader) in _readers)
            reader.Dispose();
    }
}

/// <summary>
/// Presents one producer's slot in an <see cref="IFederatedEventCursorStore"/> as a plain
/// <see cref="IEventCursorStore"/>, so a per-journal reader stays unaware that it is one of several.
/// </summary>
internal sealed class ProducerScopedCursorStore(IFederatedEventCursorStore inner, string producer)
    : IEventCursorStore
{
    /// <inheritdoc/>
    public ValueTask<EventCursor?> LoadAsync(CancellationToken token = default)
        => inner.LoadAsync(producer, token);

    /// <inheritdoc/>
    public ValueTask SaveAsync(EventCursor cursor, CancellationToken token = default)
        => inner.SaveAsync(producer, cursor, token);
}
