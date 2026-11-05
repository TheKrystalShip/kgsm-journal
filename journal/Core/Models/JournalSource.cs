namespace TheKrystalShip.KGSM.Core.Models;

/// <summary>
/// One producer's journal: who writes it, and where.
/// </summary>
/// <remarks>
/// The unit a federated reader is configured with. How a host's set of these is arrived at is not
/// this library's business — a leaf declares its journal in the descriptor it installs, and the
/// component doing the discovery passes the result in. Keeping discovery out means a journal whose
/// producer is not installed is simply not in the list, rather than something to be detected and
/// excluded.
/// </remarks>
/// <param name="Producer">The producer id (see <see cref="Events.JournalProducer"/>).</param>
/// <param name="Directory">The directory holding that producer's <c>YYYY-MM-DD.ndjson</c> segments.</param>
public sealed record JournalSource(string Producer, string Directory);
