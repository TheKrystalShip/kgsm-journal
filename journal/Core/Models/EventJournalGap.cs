namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// Why a consumer's stored position could not be honoured.
/// </summary>
public enum EventJournalGapReason
{
    /// <summary>
    /// The segment the cursor named is gone. Retention deletes segments on age alone and
    /// never consults a consumer, so a consumer absent longer than the journal's retention
    /// window finds its position deleted.
    /// </summary>
    SegmentPruned,

    /// <summary>
    /// The segment still exists but is shorter than the cursor's offset, so the bytes the
    /// cursor pointed at are no longer the bytes that were there. Something rewrote the
    /// segment rather than appending to it.
    /// </summary>
    SegmentTruncated
}

/// <summary>
/// A reported discontinuity in a consumer's view of the event journal: the point it wanted
/// to resume from no longer exists, so some events happened that this consumer will never
/// see.
/// </summary>
/// <remarks>
/// This is the journal keeping the never-fabricate rule at the transport itself. A gap is
/// surfaced rather than silently swallowed so a consumer can record that its history before
/// <see cref="ResumedAtSegment"/> is incomplete, instead of presenting a partial record as a
/// whole one. The socket transport could not express this at all — a missed event was
/// indistinguishable from an event that never happened.
/// </remarks>
/// <param name="LostSegment">The segment the stored cursor named.</param>
/// <param name="LostOffset">The byte offset within <paramref name="LostSegment"/> the consumer wanted to resume from.</param>
/// <param name="Reason">What made that position unusable.</param>
/// <param name="ResumedAtSegment">The segment reading actually resumed at, or <see langword="null"/> if the journal holds no segments at all.</param>
/// <param name="ResumedAtOffset">The byte offset reading actually resumed at.</param>
/// <param name="DetectedAt">When the gap was detected (UTC).</param>
public sealed record EventJournalGap(
    string LostSegment,
    long LostOffset,
    EventJournalGapReason Reason,
    string? ResumedAtSegment,
    long ResumedAtOffset,
    DateTimeOffset DetectedAt);
