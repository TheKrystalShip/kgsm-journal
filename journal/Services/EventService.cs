using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Implementation of the IEventService interface for handling KGSM events.
/// </summary>
/// <remarks>
/// Transport-agnostic by construction: it consumes raw envelopes from an
/// <see cref="IEventSource"/> and never knows whether they arrived over a socket or were read
/// out of the journal. That is what lets a consumer change transport without touching a single
/// handler.
/// </remarks>
public class EventService : IEventService, IAsyncDisposable
{
    private readonly IEventSource _client;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<EventService> _logger;
    private bool _disposed = false;
    private bool _initialized = false;

    /// <summary>
    /// A dictionary to hold event handlers for the different event types.
    /// </summary>
    private readonly Dictionary<Type, Delegate> _eventHandlers = new();

    /// <summary>
    /// Handlers registered via <see cref="RegisterRawHandler"/>, invoked with the full
    /// envelope for every deserialized event regardless of typed dispatch.
    /// </summary>
    private readonly List<Func<EventWrapper, EventPosition, Task>> _rawHandlers = new();

    /// <summary>
    /// Handlers registered via <see cref="RegisterGapHandler"/>, invoked when the transport
    /// reports it could not resume where this consumer left off.
    /// </summary>
    private readonly List<Func<EventJournalGap, Task>> _gapHandlers = new();

    /// <summary>
    /// Initializes a new instance of the EventService class.
    /// </summary>
    /// <param name="client">The transport delivering raw event envelopes.</param>
    /// <param name="logger">The logger to use for logging.</param>
    public EventService(IEventSource client, ILogger<EventService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();

        _logger.LogDebug("EventService initialized");
    }

    /// <summary>
    /// Finalizer for the EventService class.
    /// </summary>
    ~EventService()
    {
        Dispose(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                // Try to cancel the CancellationTokenSource
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CancellationTokenSource already disposed, ignore
            }
            finally
            {
                try
                {
                    _cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }

                try
                {
                    _client.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }

                _disposed = true;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            try
            {
                // Try to cancel if not already cancelled
                _cts.Cancel();

                // Give some time for async operations to complete
                await Task.Delay(100).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            finally
            {
                try
                {
                    _cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }

                try
                {
                    if (_client is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        _client.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }

                _disposed = true;
            }
        }
    }

    /// <inheritdoc/>
    public void Initialize() => Initialize(startPosition: null);

    /// <inheritdoc/>
    public void Initialize(EventStartPosition startPosition) => Initialize((EventStartPosition?)startPosition);

    /// <summary>
    /// Subscribes to the transport and starts it, optionally overriding where a replayable
    /// transport begins reading.
    /// </summary>
    /// <param name="startPosition">The start position to impose, or null to keep the configured one.</param>
    private void Initialize(EventStartPosition? startPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EventService));

        // Idempotent, and load-bearing that it is. Two callers legitimately reach here — KgsmClient's
        // constructor and whatever the consumer wires — and a second pass would subscribe
        // OnEventReceivedAsync to the transport AGAIN and start a SECOND read loop, so every event
        // arrived N² times: twice-subscribed × two loops = four deliveries of one event, each one
        // driving whatever the consumer does with it (an announcement, a notification, a cache bust).
        // The two loops also raced the journal reader's single cursor. Callers must not have to know
        // who else initializes.
        if (_initialized)
        {
            _logger.LogDebug("Event service already initialized — ignoring the repeat call");
            return;
        }
        _initialized = true;

        _logger.LogInformation("Initializing event service");

        _client.EventReceived += OnEventReceivedAsync;

        if (_client is IEventJournalReader journal)
        {
            if (startPosition.HasValue)
                journal.StartPosition = startPosition.Value;

            journal.GapDetected += OnGapDetectedAsync;
        }
        else if (startPosition.HasValue)
        {
            // Never quietly accept a setting the transport cannot honour: the socket has no
            // history to position within, so a caller asking to replay would get live-only
            // delivery and no indication of why.
            _logger.LogWarning(
                "Start position {StartPosition} ignored: the {Transport} transport delivers only events that arrive while it is listening",
                startPosition.Value, _client.GetType().Name);
        }

        if (_cts.IsCancellationRequested)
            throw new InvalidOperationException("Cannot initialize after disposal.");

        _logger.LogDebug("Starting event listener");

        // Start listening for events asynchronously.
        Task.Run(() => _client.StartListeningAsync(_cts.Token));

        _logger.LogInformation("Event service initialized");
    }

    /// <inheritdoc/>
    public void RegisterHandler<T>(Func<T, Task> handler) where T : KgsmEventDataBase
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EventService));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        Type eventType = typeof(T);
        _logger.LogDebug("Registering handler for event type {EventType}", eventType.Name);

        _eventHandlers[eventType] = async (KgsmEventDataBase data) => await handler((T)data).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void RegisterRawHandler(Func<EventWrapper, EventPosition, Task> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EventService));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        _logger.LogDebug("Registering raw event handler");

        _rawHandlers.Add(handler);
    }

    /// <inheritdoc/>
    public void RegisterGapHandler(Func<EventJournalGap, Task> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EventService));
        ArgumentNullException.ThrowIfNull(handler, nameof(handler));

        _logger.LogDebug("Registering event gap handler");

        _gapHandlers.Add(handler);
    }

    /// <summary>
    /// Fans a transport-reported gap out to every registered gap handler, each isolated so one
    /// throwing handler cannot stop the others or the read loop that raised it.
    /// </summary>
    /// <param name="gap">The reported discontinuity.</param>
    private async Task OnGapDetectedAsync(EventJournalGap gap)
    {
        if (_disposed)
            return;

        _logger.LogWarning(
            "Event stream gap reported at {Segment}+{Offset} ({Reason})",
            gap.LostSegment, gap.LostOffset, gap.Reason);

        foreach (Func<EventJournalGap, Task> gapHandler in _gapHandlers)
        {
            try
            {
                await gapHandler(gap).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in event gap handler");
            }
        }
    }

    /// <summary>
    /// Handles one envelope delivered by the transport.
    /// </summary>
    /// <param name="message">The raw event envelope.</param>
    /// <param name="position">Where the envelope sits in the journal, for handlers that key on it.</param>
    private async Task OnEventReceivedAsync(string message, EventPosition position)
    {
        // Ignore events after disposal
        if (_disposed)
            return;

        _logger.LogDebug("Received event message: {MessageLength} bytes", message.Length);

        try
        {
            EventWrapper? eventWrapper = JsonSerializer.Deserialize(message, JournalJsonContext.Default.EventWrapper);

                // A journal holds what earlier builds wrote for as long as retention keeps it, so a
                // line can carry a name that has since been renamed. Resolved here, once, so every
                // consumer above works in the current vocabulary and none of them carries a second
                // name for anything.
            if (eventWrapper is not null)
                eventWrapper.EventType = LegacyEventNames.Canonical(eventWrapper.EventType);

            if (eventWrapper == null || string.IsNullOrWhiteSpace(eventWrapper.EventType))
            {
                _logger.LogError("Invalid event wrapper received: {Message}", message);
                return;
            }

            _logger.LogDebug("Processing event of type {EventType}", eventWrapper.EventType);

            // The transport hands over a location, because a location is all it can know without
            // parsing. The name lives in the line, so it is joined on here — once, for every handler
            // — rather than each consumer re-reading an envelope it has already been given.
            // Null when the line carries no id, which is what a pre-id line and a producer that
            // cannot mint one both look like.
            position = position with { EventId = eventWrapper.Id };

            // Raw handlers see every envelope — known or unknown EventType — before
            // typed dispatch runs, and never suppress it.
            await InvokeRawHandlersAsync(eventWrapper, position).ConfigureAwait(false);

            // What an event deserializes into is read off the catalog, which is the one registry of
            // what the engine emits: a type that can be dispatched here is necessarily one that has
            // been classified there. A name it does not know has no payload type, and falls through
            // to the unknown branch below — the raw handlers above have already seen the envelope.
            if (KgsmEventCatalog.Describe(eventWrapper.EventType).PayloadType is Type targetType)
            {
                _logger.LogDebug("Deserializing event data to type {TargetType}", targetType.Name);

                KgsmEventDataBase? eventData = eventWrapper.Data;

                if (eventData == null)
                {
                    _logger.LogWarning("Failed to deserialize event data for type {EventType}", eventWrapper.EventType);
                    return;
                }

                // Carry the envelope's audit metadata (who/when/through-what) onto the data
                // object so handlers see it without a signature change. These live at the
                // top level of the wrapper, not inside Data, so DeserializeEventData never
                // sets them.
                eventData.Timestamp = eventWrapper.Timestamp;
                eventData.Actor = eventWrapper.Actor;
                eventData.Origin = eventWrapper.Origin;

                await InvokeHandlerAsync(eventData).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Unknown event type received: {EventType}", eventWrapper.EventType);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize event message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event message");
        }
    }

    /// <summary>
    /// Invokes every registered raw handler with the full envelope, independent of
    /// typed dispatch. Each invocation is isolated in its own try/catch so one
    /// throwing (or slow-to-fault) handler can't stop the others or the read loop.
    /// </summary>
    /// <param name="eventWrapper">The deserialized event envelope.</param>
    /// <param name="position">Where the envelope sits in the journal.</param>
    private async Task InvokeRawHandlersAsync(EventWrapper eventWrapper, EventPosition position)
    {
        foreach (Func<EventWrapper, EventPosition, Task> rawHandler in _rawHandlers)
        {
            try
            {
                await rawHandler(eventWrapper, position).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in raw event handler for {EventType}", eventWrapper.EventType);
            }
        }
    }

    /// <summary>
    /// Invokes the registered handler for the given event data.
    /// </summary>
    /// <param name="eventData">The event data to handle.</param>
    private async Task InvokeHandlerAsync(KgsmEventDataBase eventData)
    {
        Type eventType = eventData.GetType();

        if (!_eventHandlers.TryGetValue(eventType, out var handler))
        {
            _logger.LogDebug("No handler registered for event type {EventType}", eventType.Name);
            return;
        }

        _logger.LogDebug("Invoking handler for event type {EventType}", eventType.Name);

        try
        {
            await ((Func<KgsmEventDataBase, Task>)handler).Invoke(eventData).ConfigureAwait(false);

            _logger.LogDebug("Handler for {EventType} completed successfully", eventType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in event handler for {EventType}", eventType.Name);
        }
    }
}
