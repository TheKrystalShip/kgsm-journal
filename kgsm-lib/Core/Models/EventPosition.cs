namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// Where an event sits in the journal — the segment holding it and the byte offset its line
/// begins at.
/// </summary>
/// <remarks>
/// <para>
/// This is where the event <em>is</em>; <see cref="EventId"/> is what it is <em>called</em>. Until
/// every line on disk carries a name, the location does the identifying, and it is an identity
/// <b>borrowed from a promise</b>: each event is
/// one whole line, and a segment is only ever appended to and deleted whole (reference/event-journal.md). While
/// that holds, no two events share a position and no event's position changes, which is what lets
/// <see cref="TheKrystalShip.KGSM.Events.AuditId.ForPosition(string, long)"/> turn it into a stable id.
/// </para>
/// <para>
/// <b>Rewrite a segment and this breaks silently.</b> Deleting one line shifts every byte after it,
/// and a stored position then resolves to a real, parseable event that is simply not the one it named
/// — no exception, nothing malformed, and every reading derived from it as trustworthy-looking as one
/// derived from the truth. Nothing here can detect that; it is prevented upstream by the segment rule, and
/// detected downstream by a consumer that kept a baseline to compare against.
/// </para>
/// <para>
/// A consumer watching events arrive and a consumer reading history back therefore name the
/// same event the same way, with no coordination: one gets the position from the transport,
/// the other from the file, and both compute the same id.
/// </para>
/// </remarks>
/// <param name="Segment">The segment file name, e.g. <c>2026-08-07.ndjson</c>.</param>
/// <param name="Offset">The byte offset the event's line starts at.</param>
public readonly record struct EventPosition(string Segment, long Offset)
{
    /// <summary>
    /// Which producer's journal this position is in, or <see langword="null"/> when the transport
    /// did not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than a default of <c>"kgsm"</c>: a transport that does not report a producer has
    /// not told us it was the engine, and filling that in would be exactly the fabricated
    /// provenance the location-over-field rule exists to prevent. It is also the only safe spelling
    /// for a struct — <c>default(EventPosition)</c> skips property initializers, so any non-null
    /// default here would be a lie in the default value.
    /// </para>
    /// <para>
    /// Set by a transport reading more than one journal; a single-journal reader leaves it null and
    /// its caller supplies the producer it configured.
    /// </para>
    /// </remarks>
    public string? Producer { get; init; }

    /// <summary>
    /// The id the event's producer minted for it, or <see langword="null"/> when the line carries
    /// none or the transport did not read one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identity the position is only a <em>location</em> for, carried alongside it so a consumer
    /// storing a reference keeps both without re-reading the line. A stored pair is then
    /// self-checking: seek by the position, compare the id, and the rewrite that used to resolve
    /// silently to a real event of the wrong kind announces itself instead.
    /// </para>
    /// <para>
    /// <b>Null is unknown, never a mismatch</b> — see <see cref="Events.EventWrapper.Id"/>. A check
    /// that reads absence as disagreement condemns every line written before the field existed.
    /// </para>
    /// <para>
    /// It takes part in equality, like <see cref="Producer"/>: two values that disagree about which
    /// event this is are two different assertions, and equality that quietly ignored the disagreement
    /// would be the same silence the id exists to break. Addressability is
    /// <see cref="IsKnown"/> — which reads the segment alone — never a comparison against
    /// <see cref="None"/>.
    /// </para>
    /// </remarks>
    public string? EventId { get; init; }

    /// <summary>
    /// Initializes a position in a named producer's journal.
    /// </summary>
    /// <param name="producer">The producer id whose journal holds the event.</param>
    /// <param name="segment">The segment file name.</param>
    /// <param name="offset">The byte offset the event's line starts at.</param>
    public EventPosition(string producer, string segment, long offset)
        : this(segment, offset) => Producer = producer;

    /// <summary>
    /// The absence of a position, for a transport that cannot supply one. Consumers that key on
    /// position must treat it as "not addressable" rather than as a real location — it names no
    /// event, and every occurrence of it is equal to every other.
    /// </summary>
    public static readonly EventPosition None = new(string.Empty, 0);

    /// <summary>Whether this names a real journal position.</summary>
    public bool IsKnown => !string.IsNullOrEmpty(Segment);

    /// <inheritdoc/>
    public override string ToString() => IsKnown ? $"{Segment}+{Offset}" : "(no position)";
}
