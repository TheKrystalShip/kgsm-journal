using TheKrystalShip.KGSM.Core.Models;

namespace TheKrystalShip.KGSM.Core.Interfaces;

/// <summary>
/// Says which event journals this host has.
/// </summary>
/// <remarks>
/// The input to the federated readers. Behind an interface so a consumer can supply a fixed set
/// instead — a test, or a host whose layout is not the conventional one — without either reader
/// learning where the list came from.
/// </remarks>
public interface IJournalDiscovery
{
    /// <summary>
    /// The journals to read, engine first.
    /// </summary>
    /// <remarks>
    /// Called each time a consumer builds its readers rather than cached, so a leaf installed after
    /// this process started is picked up on the next construction. A returned journal is one whose
    /// producer is installed; whether its directory exists yet is not checked here, because the
    /// readers already tolerate an absent one and report it honestly per producer.
    /// </remarks>
    /// <returns>One entry per producer, with a stable order.</returns>
    IReadOnlyList<JournalSource> Discover();
}
