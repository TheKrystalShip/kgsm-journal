namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// Where a journal consumer begins reading. The choice is per-consumer and consequential:
/// a consumer that announces what it reads (a chat bot) must never replay history, while a
/// consumer that indexes what it reads must.
/// </summary>
public enum EventStartPosition
{
    /// <summary>
    /// Resume from the stored cursor; with no stored cursor, start at the end of the
    /// journal and see only events appended from now on. The default: a consumer that
    /// has never run has no basis for claiming history it never processed.
    /// </summary>
    CursorOrTail = 0,

    /// <summary>
    /// Resume from the stored cursor; with no stored cursor, replay the whole surviving
    /// journal. For a consumer that materializes the journal into an index and must be
    /// able to rebuild it.
    /// </summary>
    CursorOrOldest,

    /// <summary>
    /// Ignore any stored cursor and start at the end of the journal.
    /// </summary>
    Tail,

    /// <summary>
    /// Ignore any stored cursor and replay the whole surviving journal from its oldest
    /// segment.
    /// </summary>
    Oldest
}
