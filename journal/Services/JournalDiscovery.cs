using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Works out which event journals this host has, by finding the ones that exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>A journal is discovered because it is there, not because something claims it.</b> Every producer
/// writes to <c>&lt;its state directory&gt;/events</c>, so a directory at
/// <c>/var/lib/kgsm-watchdog/events</c> is the watchdog's journal and its producer id is
/// <c>kgsm-watchdog</c> — the same name the writer used to choose that path. The directory is therefore
/// ground truth rather than a second answer able to disagree with the writer, which is exactly what a
/// name-based convention could not be: a leaf's unit name and state directory are allowed to differ,
/// and on this host one pair does.
/// </para>
/// <para>
/// The engine falls out of the same rule — <c>/var/lib/kgsm/events</c>, producer <c>kgsm</c> — and is
/// added explicitly as well, because its journal directory is configurable and need not sit under the
/// state root at all.
/// </para>
/// <para>
/// A producer that has never written an event has no directory and is simply absent. That is the honest
/// answer: there is nothing to read, and listing it as an unreadable journal would report a leaf's
/// silence as a failure to read it.
/// </para>
/// <para>
/// The scan finds a journal only where the writer's own default puts it. A producer configured to
/// write somewhere else is invisible to it — so a consumer that knows of such a journal names it
/// explicitly instead, which is how the engine's configurable directory is handled and how a consumer
/// that keeps its OWN journal at a configured path makes it readable. A named journal is taken on the
/// caller's word: nothing else on the host can know it is there.
/// </para>
/// </remarks>
public sealed class JournalDiscovery : IJournalDiscovery
{
    /// <summary>Where the ecosystem's per-service state directories live.</summary>
    /// <remarks>
    /// The writer's own constant. Where a journal lives is one fact shared by the component that
    /// creates it and the scan that finds it, so it has one definition — a second copy here would be
    /// free to drift from the paths actually being written.
    /// </remarks>
    public const string DefaultStateRoot = JournalLayout.DefaultStateRoot;

    /// <summary>The subdirectory of a state directory that holds a producer's journal segments.</summary>
    public const string JournalSubdirectory = JournalLayout.Subdirectory;

    private readonly string _engineJournalDirectory;
    private readonly string _stateRoot;
    private readonly IReadOnlyList<JournalSource> _named;
    private readonly ILogger<JournalDiscovery> _logger;
    private readonly Lock _gate = new();
    private IReadOnlyList<JournalSource>? _discovered;

    /// <summary>
    /// Initializes discovery over an engine journal and a state root.
    /// </summary>
    /// <param name="engineJournalDirectory">Where kgsm's own journal segments live.</param>
    /// <param name="stateRoot">
    /// The directory holding each producer's state directory. Null uses <see cref="DefaultStateRoot"/>.
    /// </param>
    /// <param name="logger">The logger to use.</param>
    /// <param name="named">
    /// Journals the caller knows about that the scan would not find — a producer writing somewhere
    /// other than its own state directory. The obvious case is a consumer that keeps its own journal
    /// at a configured path: it would otherwise write a record it then could not read back.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the engine journal directory is blank.</exception>
    public JournalDiscovery(
        string engineJournalDirectory, string? stateRoot, ILogger<JournalDiscovery> logger,
        IReadOnlyList<JournalSource>? named = null)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(engineJournalDirectory, nameof(engineJournalDirectory));

        _engineJournalDirectory = engineJournalDirectory;
        _stateRoot = string.IsNullOrWhiteSpace(stateRoot) ? DefaultStateRoot : stateRoot;
        _named = named ?? [];
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Scanned once; every caller gets that one answer.</b> A process builds its history reader and
    /// its live tail from this, and the two have to be reading the same set of producers — a second
    /// scan is a second chance to disagree, and a journal appearing between them would leave one half
    /// of a consumer permanently blind to a producer the other half reports on. The set is a property
    /// of the host, so answering it once per process is also what the callers already assumed.
    /// </remarks>
    public IReadOnlyList<JournalSource> Discover()
    {
        if (_discovered is { } already)
            return already;

        lock (_gate)
        {
            return _discovered ??= Scan();
        }
    }

    private IReadOnlyList<JournalSource> Scan()
    {
        // The engine first, and unconditionally: it is the one producer that is not a leaf, its journal
        // directory is configurable, and a host with an engine always has somewhere it writes.
        var sources = new List<JournalSource>
        {
            new(JournalProducer.Kgsm, _engineJournalDirectory),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal) { JournalProducer.Kgsm };

        // Named before scanned, for the same reason the engine is: a caller that says where a producer
        // writes knows better than a directory that happens to share the name.
        foreach (JournalSource declared in _named)
        {
            if (JournalProducer.IsValid(declared.Producer) && seen.Add(declared.Producer))
                sources.Add(declared);
        }

        foreach (JournalSource found in ScanStateRoot())
        {
            // A producer named twice would make the ids derived from its two journals collide, which is
            // the one thing the producer prefix exists to prevent. The engine's configured directory
            // wins over whatever the scan finds under its name.
            if (seen.Add(found.Producer))
                sources.Add(found);
        }

        _logger.LogDebug(
            "Discovered {Count} event journals: {Producers}",
            sources.Count, string.Join(", ", sources.Select(static s => s.Producer)));

        return sources;
    }

    /// <summary>
    /// Every <c>&lt;state-dir&gt;/events</c> directory under the state root, in a stable order.
    /// </summary>
    /// <remarks>
    /// Sorted by producer so a merged page's cross-journal tie-break is the same on every host and every
    /// restart. Directory enumeration order is not guaranteed, and an ordering that varied per process
    /// would make two readers of the same record disagree about which of two simultaneous events came
    /// first.
    /// </remarks>
    private IEnumerable<JournalSource> ScanStateRoot()
    {
        string[] candidates;

        try
        {
            if (!Directory.Exists(_stateRoot))
            {
                _logger.LogDebug(
                    "No state root at {Root}; only the engine journal is known", _stateRoot);
                return [];
            }

            // Only the ecosystem's own state directories are considered. A
            // producer id has to be a usable one anyway, but narrowing the scan keeps it from stat-ing
            // every service on the host.
            candidates = [.. JournalProducer.ScannedPrefixes
                .SelectMany(prefix => Directory.GetDirectories(_stateRoot, prefix + "*"))
                .Distinct(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "Could not list {Root}; only the engine journal is known", _stateRoot);
            return [];
        }

        var found = new List<JournalSource>(candidates.Length);

        foreach (string stateDirectory in candidates)
        {
            string producer = Path.GetFileName(stateDirectory);
            string journal = Path.Combine(stateDirectory, JournalSubdirectory);

            if (!JournalProducer.IsValid(producer))
            {
                _logger.LogDebug(
                    "Skipping {Directory}: '{Producer}' is not a usable journal producer id",
                    stateDirectory, producer);
                continue;
            }

            // A producer with no journal directory has written no event. Absent, not unreadable.
            if (!Directory.Exists(journal))
                continue;

            found.Add(new JournalSource(producer, journal));
        }

        found.Sort(static (a, b) => string.CompareOrdinal(a.Producer, b.Producer));
        return found;
    }
}
