namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// What a reader of the event journal needs: where the engine's journal is, where a cursor is kept, where
/// a reader with no cursor begins and how far a history query may scan.
/// </summary>
public class JournalReadOptions
{
    /// <summary>Where the engine writes its journal, when no setting says otherwise.</summary>
    public const string DefaultEventJournalDirectory = "/var/lib/kgsm/events";

    /// <summary>The default bytes a history query may scan in one journal before it reports a truncated answer.</summary>
    public const long DefaultEventHistoryScanBudgetBytes = 64L * 1024 * 1024;

    /// <summary>The directory holding the engine's <c>YYYY-MM-DD.ndjson</c> segments.</summary>
    public string EventJournalDirectory { get; set; } = DefaultEventJournalDirectory;

    /// <summary>Where the single-journal cursor is kept. Null or blank keeps no position.</summary>
    public string? EventCursorPath { get; set; }

    /// <summary>Where a reader with no stored position begins.</summary>
    public EventStartPosition EventStartPosition { get; set; } = EventStartPosition.CursorOrTail;

    /// <summary>The bytes a history query may scan per journal. Non-positive uses the default.</summary>
    public long EventHistoryScanBudgetBytes { get; set; } = DefaultEventHistoryScanBudgetBytes;
}
