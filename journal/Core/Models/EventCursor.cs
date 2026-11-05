namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// A consumer's position in the KGSM event journal: the segment it is reading and the
/// byte offset within that segment of the first line it has NOT yet processed.
/// </summary>
/// <remarks>
/// A byte offset works because a journal segment is append-only and every event occupies
/// exactly one line — the engine writes payloads compact for that reason. Anything that
/// rewrites a segment in place (a log rotator's <c>copytruncate</c>, an editor) invalidates
/// every cursor pointing into it; retention therefore deletes whole segments and never
/// truncates one.
/// </remarks>
public sealed record EventCursor
{
    /// <summary>
    /// The segment file name, without a directory (e.g. <c>2026-08-04.ndjson</c>). Segment
    /// names sort lexically in chronological order.
    /// </summary>
    public string Segment { get; init; } = string.Empty;

    /// <summary>
    /// The byte offset of the next unread line. <c>0</c> is the start of the segment; the
    /// segment's length means everything written so far has been processed.
    /// </summary>
    public long Offset { get; init; }
}
