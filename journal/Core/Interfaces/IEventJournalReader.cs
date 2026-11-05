using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Tails the KGSM event journal — the append-only NDJSON files the engine writes, one line
/// per event, in date-named daily segments.
/// </summary>
/// <remarks>
/// Unlike a socket, a file imposes no coordination between readers: any number of consumers
/// tail the same journal at their own pace, and the engine holds no knowledge of who reads.
/// A consumer that was down catches up from its cursor instead of missing what it slept
/// through — bounded only by the engine's retention window, past which the reader reports an
/// <see cref="EventJournalGap"/> rather than pretending to have full history.
/// </remarks>
public interface IEventJournalReader : IEventSource
{
    /// <summary>
    /// The directory the segments are read from.
    /// </summary>
    string JournalDirectory { get; }

    /// <summary>
    /// Where reading begins. Must be set before <see cref="IEventSource.StartListeningAsync"/>
    /// runs; changing it afterwards has no effect on the reader already in flight.
    /// </summary>
    EventStartPosition StartPosition { get; set; }

    /// <summary>
    /// Raised when a stored position could not be honoured, before reading resumes at the
    /// fallback position the gap reports. A consumer that persists history should record
    /// this, so it can state that its record before that point is incomplete instead of
    /// implying coverage it does not have.
    /// </summary>
    event Func<EventJournalGap, Task>? GapDetected;
}
