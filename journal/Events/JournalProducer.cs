namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// The identity of a component that writes an event journal.
/// </summary>
/// <remarks>
/// <para>
/// A producer id is never read out of an event's own payload. It comes from <em>which journal a
/// line was read from</em>, which a reader establishes itself and can therefore check — where a
/// field inside the data is a claim it cannot. That is what stops one producer's line from
/// presenting itself as another's.
/// </para>
/// <para>
/// This class holds the format rule and the one producer the library knows by name. It is
/// deliberately <b>not</b> a registry of leaves: which journals exist on a host is discovered from
/// the installed leaf descriptors, so a list here would be a second answer able to disagree with
/// the host.
/// </para>
/// </remarks>
public static class JournalProducer
{
    /// <summary>
    /// The engine's own journal. Known by name because kgsm is the engine rather than a leaf —
    /// it is present wherever the library is useful at all.
    /// </summary>
    public const string Kgsm = "kgsm";

    /// <summary>The prefix every component in this ecosystem carries.</summary>
    public const string EcosystemPrefix = Kgsm + "-";

    /// <summary>
    /// The beginnings of the state directories a reader scans: the engine and this ecosystem's
    /// components.
    /// </summary>
    public static IReadOnlyList<string> ScannedPrefixes { get; } = [Kgsm];

    /// <summary>
    /// Whether a reader scanning a state root finds <paramref name="producer"/>'s journal at all.
    /// </summary>
    /// <param name="producer">The producer id.</param>
    /// <returns>True when its state directory begins with one of <see cref="ScannedPrefixes"/>.</returns>
    public static bool IsScanned(string producer)
    {
        foreach (string prefix in ScannedPrefixes)
        {
            if (producer.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The actor provider an autonomous component attributes its own actions to.</summary>
    public const string SystemActorProvider = "system";

    /// <summary>
    /// The actor <paramref name="producer"/> stamps on an action it took by itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>provider:name</c> is the convention every consumer parses, and it is not decoration: a bare
    /// name reads as an OS user, so an autonomous emitter that wrote one would present itself as a
    /// person on the local host. The provider is <see cref="SystemActorProvider"/> and the name is
    /// the producer with its ecosystem prefix dropped, since the prefix says only that it belongs to
    /// this ecosystem — which the reader already knows from the journal it read.
    /// </para>
    /// <para>
    /// Derived rather than declared per component, so a producer's identity has one source. An actor
    /// held as a constant beside the producer id is a second spelling of the same fact, free to
    /// disagree with it.
    /// </para>
    /// </remarks>
    /// <param name="producer">The producer id.</param>
    /// <returns>The <c>system:&lt;name&gt;</c> actor for that producer.</returns>
    /// <exception cref="ArgumentException">Thrown when the producer id is unusable.</exception>
    public static string SystemActorFor(string producer)
    {
        Validate(producer, nameof(producer));

        string name = producer.Length > EcosystemPrefix.Length
            && producer.StartsWith(EcosystemPrefix, StringComparison.Ordinal)
                ? producer[EcosystemPrefix.Length..]
                : producer;

        return $"{SystemActorProvider}:{name}";
    }

    /// <summary>
    /// Whether <paramref name="producer"/> is a usable producer id.
    /// </summary>
    /// <remarks>
    /// Lowercase letters, digits and dashes, and no underscore. The underscore is excluded because it
    /// separates the fields of a position id (<c>evt_&lt;producer&gt;_&lt;segment&gt;_&lt;offset&gt;</c>),
    /// and a producer containing one would make an id ambiguous to read back.
    /// </remarks>
    /// <param name="producer">The candidate id.</param>
    /// <returns>True when it can be used.</returns>
    public static bool IsValid(string? producer)
    {
        if (string.IsNullOrEmpty(producer))
            return false;

        foreach (char c in producer)
        {
            bool ok = c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-';
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Throws unless <paramref name="producer"/> is valid.
    /// </summary>
    /// <param name="producer">The candidate id.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">Thrown when the id is unusable.</exception>
    public static void Validate(string? producer, string paramName)
    {
        if (!IsValid(producer))
        {
            throw new ArgumentException(
                $"'{producer}' is not a valid journal producer id: expected lowercase letters, digits "
                + "and dashes, with no underscore.",
                paramName);
        }
    }
}
