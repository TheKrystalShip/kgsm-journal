namespace TheKrystalShip.KGSM.Events;

/// <summary>
/// Where a producer's journal sits, and what a reader derives from finding it there.
/// </summary>
/// <remarks>
/// <para>
/// A producer's <b>id</b>, its <b>state directory</b> and the <b>directory it appends to</b> are one
/// fact spelled three ways. A reader establishes the producer from the path it read a line out of, so
/// the writer choosing a path and the reader naming a producer have to be the same rule — and the only
/// way two implementations of one rule stay agreed is for there to be one implementation.
/// <see cref="DirectoryFor"/> composes it and <see cref="ProducerOf"/> inverts it, so a writer can ask
/// what a reader will conclude about it rather than assuming.
/// </para>
/// <para>
/// The failure this exists to prevent is silent. A producer writing where no reader scans is not
/// reported as unreadable or degraded: the directory simply is not found, and a producer with no
/// directory has honestly recorded nothing. A misplaced journal and an idle leaf look identical.
/// </para>
/// </remarks>
public static class JournalLayout
{
    /// <summary>Where the ecosystem's per-service state directories live.</summary>
    public const string DefaultStateRoot = "/var/lib";

    /// <summary>The subdirectory of a state directory holding a producer's journal segments.</summary>
    public const string Subdirectory = "events";

    /// <summary>
    /// The directory <paramref name="producer"/> appends its segments to.
    /// </summary>
    /// <remarks>
    /// A producer's own state directory, so uninstalling the component takes its journal with it and
    /// no producer needs write access to another's.
    /// </remarks>
    /// <param name="producer">The producer id.</param>
    /// <param name="stateRoot">
    /// Where state directories live. Null uses <see cref="DefaultStateRoot"/>. A relocated root is
    /// what a test isolates itself with, and it changes nothing about the rule.
    /// </param>
    /// <returns>The journal directory for that producer.</returns>
    /// <exception cref="ArgumentException">Thrown when the producer id is unusable.</exception>
    public static string DirectoryFor(string producer, string? stateRoot = null)
    {
        JournalProducer.Validate(producer, nameof(producer));

        string root = string.IsNullOrWhiteSpace(stateRoot) ? DefaultStateRoot : stateRoot;
        return Path.Combine(root, producer, Subdirectory);
    }

    /// <summary>
    /// The producer a reader concludes from finding a journal at <paramref name="journalDirectory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inverse of <see cref="DirectoryFor"/>, and deliberately as strict as the scan that finds
    /// journals on a host: the last path segment has to be <see cref="Subdirectory"/>, the one before
    /// it has to be a usable producer id, and it has to be <see cref="JournalProducer.IsScanned"/> — a
    /// scan narrowed to the ecosystem's own state directories never reaches anything else.
    /// </para>
    /// <para>
    /// Null means <b>no reader will attribute a line here to any producer</b>, which is the answer a
    /// writer needs before it writes rather than after.
    /// </para>
    /// </remarks>
    /// <param name="journalDirectory">A candidate journal directory.</param>
    /// <returns>The producer id a reader derives, or null when a reader would not find it at all.</returns>
    public static string? ProducerOf(string? journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory))
            return null;

        // Trailing separators would otherwise make the last segment empty and the check answer "no
        // producer" for a path that is perfectly conventional.
        string trimmed = journalDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.Equals(Path.GetFileName(trimmed), Subdirectory, StringComparison.Ordinal))
            return null;

        string? stateDirectory = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(stateDirectory))
            return null;

        string producer = Path.GetFileName(stateDirectory);

        if (!JournalProducer.IsValid(producer))
            return null;

        return JournalProducer.IsScanned(producer) ? producer : null;
    }
}
