using System.Globalization;
using TheKrystalShip.KGSM.Conformance;

namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// Derives a stable id for a KGSM event — no <see cref="Guid"/>, no randomness, so two
/// independent computations of the same event agree with zero coordination between them.
/// That is what lets one component recognize another's "this is the same event" without a
/// shared sequence, a database round-trip, or a network call.
/// </summary>
/// <remarks>
/// <see cref="ForPosition(string, long)"/> keys an event by where it sits in the journal: unique by
/// construction and ordered like the file, so it is what the audit trail is keyed on and what paginates it.
/// </remarks>
public static class AuditId
{
    /// <summary>
    /// Composes the id for the event stored at a journal position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id is <c>"evt_"</c>, the segment's name without its extension, an underscore, and the
    /// byte offset the event's line begins at, zero-padded to 12 digits —
    /// <c>evt_2026-08-07_000000001234</c>. Two properties follow from that and both are relied
    /// on. It is <b>unique</b>, because no two lines start at the same offset in the same
    /// segment; unlike a content-derived id it cannot merge two events that happen to be
    /// identical within the same second. And it is <b>ordered</b>: segment names are equal-width
    /// dates and the offset is fixed-width, so comparing two ids as plain strings compares their
    /// positions in the journal. That is what lets one value serve as both the event's identity
    /// and a pagination cursor.
    /// </para>
    /// <para>
    /// The padding is what makes the ordering work — <c>9</c> sorts after <c>10</c> as text, but
    /// <c>000000000009</c> does not. Twelve digits addresses a segment far larger than a day of
    /// events could ever produce; an offset beyond it still yields a correct, unique id and only
    /// loses the string-ordering property.
    /// </para>
    /// </remarks>
    /// <param name="segment">The segment file name, with or without its <c>.ndjson</c> extension.</param>
    /// <param name="offset">The byte offset the event's line starts at.</param>
    /// <returns>An id of the form <c>evt_&lt;segment&gt;_&lt;offset&gt;</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="segment"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative.</exception>
    public static string ForPosition(string segment, long offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment, nameof(segment));
        ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));

        return "evt_" + StemOf(segment) + "_" + offset.ToString("D12", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Composes the id for the event stored at a position in a named producer's journal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The producer goes <b>first</b> — <c>evt_watchdog_2026-08-07_000000001234</c> — so that the
    /// ordering property of <see cref="ForPosition(string, long)"/> survives federation. Compared
    /// as plain strings, these ids sort by <c>(producer, segment, offset)</c>, which is exactly the
    /// documented cross-journal tie-break: no host-local mechanism can order two independent
    /// appends inside one millisecond, so the order is deterministic rather than true.
    /// </para>
    /// <para>
    /// That only works because the <em>primary</em> sort is the timestamp and this id is the
    /// tie-break within one instant — see <see cref="Core.Models.EventHistoryQuery.BeforeTsMs"/>
    /// and its <see cref="Core.Models.EventHistoryQuery.BeforeId"/> companion. An id compared
    /// across producers as the primary key would group by producer, not by time.
    /// </para>
    /// <para>
    /// The two-argument overload produces the unprefixed form and stays the right call for the
    /// engine's own journal read on its own. The two forms are different ids for the same event,
    /// so a given reader must use one of them consistently.
    /// </para>
    /// </remarks>
    /// <param name="producer">The journal's producer id (see <see cref="JournalProducer"/>).</param>
    /// <param name="segment">The segment file name, with or without its <c>.ndjson</c> extension.</param>
    /// <param name="offset">The byte offset the event's line starts at.</param>
    /// <returns>An id of the form <c>evt_&lt;producer&gt;_&lt;segment&gt;_&lt;offset&gt;</c>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="producer"/> is not a valid producer id, or when
    /// <paramref name="segment"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative.</exception>
    public static string ForPosition(string producer, string segment, long offset)
    {
        JournalProducer.Validate(producer, nameof(producer));
        ArgumentException.ThrowIfNullOrWhiteSpace(segment, nameof(segment));
        ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));

        return "evt_" + producer + "_" + StemOf(segment) + "_" + offset.ToString("D12", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The id for one journal line: its own name when it has one, else where it sits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A line's minted id is the better identity and this prefers it.</b> A position is right only
    /// while a segment is appended to and deleted whole (reference/event-journal.md); delete one line and every
    /// id after it silently becomes the id of a different event. An id is the line's own name, so it
    /// survives that — the row keeps its identity and the rewrite shows up as a position that no longer
    /// resolves, which is a fault somebody can see.
    /// </para>
    /// <para>
    /// <b>Every derivation of an event's id must make this same choice.</b> One event served two ways
    /// — pushed live and found in history — has to come back with one id, or a client reconciling the
    /// two sees two facts. That is why this takes the id as an argument rather than reading it
    /// somewhere: both callers hold it already, and neither can quietly opt out.
    /// </para>
    /// <para>
    /// <b>Ordering survives.</b> The id is the tie-break in the <c>(timestamp, id)</c> keyset a page
    /// walks, compared as a plain string. A UUIDv7 is time-ordered, so within a producer these sort the
    /// way the journal does; across producers, within a single millisecond, the order is deterministic
    /// rather than true — which is exactly what the positional form gave. The tie-break's real
    /// requirement is only that it is a <em>stable total order both sources of a merged page agree
    /// on</em>: a caller merges this history with rows that have no journal position at all, so the
    /// comparison can never be positional. Mixed forms compare fine, which is what lets the two
    /// coexist for as long as retention holds a line written before ids existed.
    /// </para>
    /// </remarks>
    /// <param name="eventId">The line's own id, or null when it carries none.</param>
    /// <param name="segment">The segment file name, with or without its <c>.ndjson</c> extension.</param>
    /// <param name="offset">The byte offset the event's line starts at.</param>
    /// <returns>
    /// <c>evt_&lt;id&gt;</c> when the line is named, otherwise <see cref="ForPosition(string, long)"/>.
    /// </returns>
    public static string ForLine(string? eventId, string segment, long offset) =>
        Named(eventId) ?? ForPosition(segment, offset);

    /// <inheritdoc cref="ForLine(string?, string, long)"/>
    /// <param name="eventId">The line's own id, or null when it carries none.</param>
    /// <param name="producer">The journal's producer id (see <see cref="JournalProducer"/>).</param>
    /// <param name="segment">The segment file name, with or without its <c>.ndjson</c> extension.</param>
    /// <param name="offset">The byte offset the event's line starts at.</param>
    /// <remarks>
    /// The producer-prefixed fallback. A named line needs no producer in its id — the name is already
    /// unique across every journal on the host, which is the property the prefix was compensating for.
    /// </remarks>
    public static string ForLine(string? eventId, string producer, string segment, long offset) =>
        Named(eventId) ?? ForPosition(producer, segment, offset);

    /// <summary>
    /// One line's minted id as an audit id, or null when it has none or the id is not well formed.
    /// </summary>
    /// <remarks>
    /// The shape is checked rather than trusted, and falling back to the position is the right failure:
    /// an id this ecosystem did not write cannot be assumed to be unique or ordered, and building an
    /// audit id on one would put a duplicate or a mis-sort into the page. A producer writing such an id
    /// is reported by <c>envelope.event-id-shape</c>, which is where that belongs.
    /// </remarks>
    private static string? Named(string? eventId) =>
        JournalConformance.IsWellFormedEventId(eventId) ? "evt_" + eventId : null;

    /// <summary>
    /// Reads a journal position back out of an id produced by <see cref="ForPosition(string, long)"/>.
    /// </summary>
    /// <remarks>
    /// A producer-prefixed id (<see cref="ForPosition(string, string, long)"/>) parses here too, with
    /// the producer left in <paramref name="segment"/> — use
    /// <see cref="TryParseProducerPosition"/> when the producer matters.
    /// </remarks>
    /// <param name="id">The id to parse.</param>
    /// <param name="segment">The segment stem, without its extension.</param>
    /// <param name="offset">The byte offset.</param>
    /// <returns>
    /// True when <paramref name="id"/> is a position id. False for anything else, leaving the
    /// caller to treat it as an unusable cursor rather than a wrong one.
    /// </returns>
    public static bool TryParsePosition(string? id, out string segment, out long offset)
    {
        segment = string.Empty;
        offset = 0;

        if (string.IsNullOrEmpty(id) || !id.StartsWith("evt_", StringComparison.Ordinal))
            return false;

        // The stem may itself contain underscores, so the offset is taken from the LAST one.
        int split = id.LastIndexOf('_');
        if (split <= 3)
            return false;

        if (!long.TryParse(id.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out offset))
            return false;

        segment = id[4..split];
        return segment.Length > 0;
    }

    /// <summary>
    /// Reads a producer and journal position back out of an id produced by
    /// <see cref="ForPosition(string, string, long)"/>.
    /// </summary>
    /// <remarks>
    /// False for an unprefixed position id: a caller asking which producer wrote an event must get
    /// "this id does not say" rather than a guess. The producer is the first field and carries no underscore
    /// (<see cref="JournalProducer.IsValid"/>), so the split is exact.
    /// </remarks>
    /// <param name="id">The id to parse.</param>
    /// <param name="producer">The producer id.</param>
    /// <param name="segment">The segment stem, without its extension.</param>
    /// <param name="offset">The byte offset.</param>
    /// <returns>True when <paramref name="id"/> is a producer-prefixed position id.</returns>
    public static bool TryParseProducerPosition(
        string? id, out string producer, out string segment, out long offset)
    {
        producer = string.Empty;
        segment = string.Empty;
        offset = 0;

        if (!TryParsePosition(id, out string rest, out long parsedOffset))
            return false;

        // `rest` is everything between "evt_" and the offset — "<producer>_<segment>" for a
        // prefixed id, and a bare segment otherwise. The producer cannot contain an underscore, so
        // the FIRST one is the boundary; a value with none is an unprefixed id and reports false.
        int split = rest.IndexOf('_');
        if (split <= 0 || split == rest.Length - 1)
            return false;

        string candidate = rest[..split];
        string remainder = rest[(split + 1)..];

        // The remainder must be a date, the engine's segment naming. Without that check an
        // unprefixed id over an unconventionally-named segment ("evt_my_segment_000…") would read
        // its first token as a producer and answer confidently wrongly. Failing closed here leaves
        // the caller with "this id does not say", which it already has to handle.
        if (!JournalProducer.IsValid(candidate) || !DateOnly.TryParse(remainder, out _))
            return false;

        producer = candidate;
        segment = remainder;
        offset = parsedOffset;
        return true;
    }

    /// <summary>The segment file name without its extension.</summary>
    private static string StemOf(string segment)
    {
        string name = Path.GetFileName(segment);
        return name.EndsWith(".ndjson", StringComparison.Ordinal) ? name[..^7] : name;
    }
}
