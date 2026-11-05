using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Extensions;

/// <summary>
/// Registers a component's own event journal writer.
/// </summary>
/// <remarks>
/// Being a producer is four decisions — which name to write under, which directory, which version to
/// stamp, and when the directory comes into existence — and every one of them has a single right
/// answer that a producer can derive rather than choose. Made once here so no repo makes them again.
/// </remarks>
public static class JournalServiceCollectionExtensions
{
    /// <summary>
    /// The variable that relocates every producer's journal on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deriving the journal path from the producer id is what keeps a writer and every reader agreed
    /// on it — and it also means a component run by hand writes exactly where the deployed one does.
    /// A developer running a leaf from a checkout, or a test suite exercising one, would append to the
    /// host's real audit record. <b>Fabricated history is worse than none</b>, so there has to be one
    /// way to move the whole layout aside.
    /// </para>
    /// <para>
    /// One variable rather than a per-leaf setting, because it is one concern: a process either writes
    /// to this host's journals or to a throwaway root, and that is never a question about a particular
    /// leaf. It relocates the <em>root</em> and never the rule — a producer keeps its own name and its
    /// own <c>events</c> subdirectory under it, so a relocated journal is still readable by pointing a
    /// reader's state root at the same place.
    /// </para>
    /// </remarks>
    public const string StateRootVariable = "KGSM_JOURNAL_STATE_ROOT";

    /// <summary>
    /// Registers <see cref="IEventJournalWriter"/> for <paramref name="producer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The producer id decides the directory (<see cref="JournalLayout.DirectoryFor"/>), which is what
    /// keeps the writer and every reader agreed on where this journal is without either being told.
    /// The version comes from <paramref name="versionSource"/> through
    /// <see cref="ProducerVersion.Of"/>, so one build identity reaches every event.
    /// </para>
    /// <para>
    /// The journal directory is created here, at registration, rather than on the first event: a
    /// consumer discovers producers when it starts, so a producer whose directory appears later is
    /// invisible until it emits <em>and</em> every consumer restarts. Creating it during startup means
    /// a deployed producer is discoverable from the moment it runs, whether or not it has yet had
    /// anything to record.
    /// </para>
    /// <para>
    /// <paramref name="configure"/> is for the producer whose journal genuinely does not sit at the
    /// convention — a configurable path, or a test isolating itself under a temporary root. It is not
    /// the normal case, and a directory a reader would attribute to somebody else is reported by the
    /// writer at construction rather than accepted quietly.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="producer">
    /// This component's producer id — its state directory's own name, which is what a reader derives.
    /// </param>
    /// <param name="versionSource">
    /// The assembly whose version identifies this build. Passed explicitly rather than inferred from
    /// the call stack, which inlining and AOT both make unreliable.
    /// </param>
    /// <param name="stateRoot">
    /// Where state directories live. Null reads <see cref="StateRootVariable"/>, and falls back to
    /// <see cref="JournalLayout.DefaultStateRoot"/> when that is unset.
    /// </param>
    /// <param name="configure">Adjusts the options before the writer is built. Optional.</param>
    /// <returns>The service collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="versionSource"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the producer id is unusable.</exception>
    public static IServiceCollection AddKgsmJournal(
        this IServiceCollection services,
        string producer,
        Assembly versionSource,
        string? stateRoot = null,
        Action<EventJournalWriterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(versionSource, nameof(versionSource));
        JournalProducer.Validate(producer, nameof(producer));

        string root = stateRoot
            ?? Environment.GetEnvironmentVariable(StateRootVariable)
            ?? JournalLayout.DefaultStateRoot;

        var options = new EventJournalWriterOptions
        {
            Producer = producer,
            Directory = JournalLayout.DirectoryFor(producer, root),
            ProducerVersion = ProducerVersion.Of(versionSource),
        };

        configure?.Invoke(options);
        options.Validate();

        // Before the container is built, so there is no logger to report a failure to. The writer
        // creates it as well and says so properly if it cannot, which is the right moment: a
        // permission problem matters when it costs an event, and it will cost one then too.
        TryCreateDirectory(options.Directory!);

        services.AddSingleton<IEventJournalWriter>(sp => new EventJournalWriter(
            options, sp.GetRequiredService<ILogger<EventJournalWriter>>()));

        return services;
    }

    /// <summary>
    /// Registers a reader of the engine's journal: the event source, the event service, the cursor store
    /// and the history query.
    /// </summary>
    /// <remarks>
    /// A consumer that keeps its cursor in its own database registers its own <see cref="IEventCursorStore"/>
    /// after this call, which wins by last-registration. <see cref="AddKgsmJournalFederation"/> replaces the
    /// single-journal source and history with the federated pair whichever order the two are called in.
    /// </remarks>
    public static IServiceCollection AddKgsmJournalReader(this IServiceCollection services, JournalReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        services.AddSingleton(options);
        services.AddSingleton<IEventCursorStore>(sp => string.IsNullOrWhiteSpace(options.EventCursorPath)
            ? new NullEventCursorStore()
            : new FileEventCursorStore(options, sp.GetRequiredService<ILogger<FileEventCursorStore>>()));
        services.AddSingleton<IEventJournalReader, EventJournalReader>();
        services.AddSingleton<EventJournalHistory>();
        AddJournalResolution(services);
        return services;
    }

    /// <summary>
    /// Reads every producer's event journal instead of only the engine's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <b>after</b> <see cref="AddKgsmJournalReader(IServiceCollection, JournalReadOptions)"/>: this replaces
    /// that call's <see cref="IEventJournalHistory"/> and <see cref="IEventSource"/> with the
    /// federated pair, by last-registration. Every handler a consumer has registered keeps working
    /// unchanged — <c>EventService</c> resolves <see cref="IEventSource"/> and never learns what backs
    /// it, which is the whole point of that indirection.
    /// </para>
    /// <para>
    /// <b>Called before it, this does nothing and says nothing.</b> The later
    /// <c>AddKgsmServices</c> re-registers the single-journal pair over this one, leaving a consumer
    /// tailing the engine alone while believing it tails every producer — which reads as a quiet host
    /// rather than as a misconfiguration. There is no error to catch: both registrations are valid, and
    /// only the order decides which wins.
    /// </para>
    /// <para>
    /// Which journals exist is discovered by finding them on disk, so this needs no list of leaves and a
    /// leaf that starts writing one later costs no rebuild. A consumer whose host is laid out
    /// unconventionally registers its own <see cref="IJournalDiscovery"/> after this call.
    /// </para>
    /// <para>
    /// The federated source keeps <b>one cursor per producer</b>, which is a different store from the
    /// single-journal <see cref="IEventCursorStore"/> and cannot be migrated from it: a position in the
    /// engine's journal says nothing about a position in anyone else's. A consumer switching over
    /// therefore starts each journal from <paramref name="startPosition"/>, and one that indexes events
    /// wants <see cref="EventStartPosition.CursorOrOldest"/> so it can rebuild.
    /// </para>
    /// </remarks>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="cursorPath">
    /// Where the per-producer cursor map is kept. Null or blank keeps **no** positions, so every
    /// journal starts from <paramref name="startPosition"/> on every run — what a consumer that
    /// announces events onward rather than deriving durable state from them actually wants, since
    /// resuming would re-announce a backlog.
    /// </param>
    /// <param name="startPosition">Where a journal with no stored position begins.</param>
    /// <param name="engineJournalDirectory">
    /// Where kgsm's own journal lives. Null uses <see cref="JournalReadOptions.DefaultEventJournalDirectory"/>.
    /// </param>
    /// <param name="stateRoot">
    /// Where each producer's state directory lives, each holding its journal in an <c>events</c>
    /// subdirectory. Null uses <see cref="JournalDiscovery.DefaultStateRoot"/>.
    /// </param>
    /// <param name="scanBudgetBytes">
    /// The scan budget allowed for each journal in a history query. Non-positive uses
    /// <see cref="JournalReadOptions.DefaultEventHistoryScanBudgetBytes"/>. It applies <em>per journal</em>,
    /// so each answers to the same depth whatever the fleet size, at the cost of total work scaling
    /// with the number of producers.
    /// </param>
    /// <param name="namedJournals">
    /// Journals to read that the scan would not find, because their producer writes somewhere other
    /// than its own state directory. The case that needs it is a consumer whose OWN journal path is
    /// configurable: left to the scan, it would write a record it could not then read back.
    /// </param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    /// <exception cref="ArgumentException">Thrown when cursorPath is null, empty, or whitespace.</exception>
    public static IServiceCollection AddKgsmJournalFederation(
        this IServiceCollection services,
        string? cursorPath = null,
        EventStartPosition startPosition = EventStartPosition.CursorOrOldest,
        string? engineJournalDirectory = null,
        string? stateRoot = null,
        long scanBudgetBytes = 0,
        IReadOnlyList<JournalSource>? namedJournals = null)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));

        long budget = scanBudgetBytes > 0 ? scanBudgetBytes : JournalReadOptions.DefaultEventHistoryScanBudgetBytes;

        services.AddSingleton<IJournalDiscovery>(sp => new JournalDiscovery(
            engineJournalDirectory ?? JournalReadOptions.DefaultEventJournalDirectory,
            stateRoot,
            sp.GetRequiredService<ILogger<JournalDiscovery>>(),
            namedJournals));

        services.AddSingleton<IFederatedEventCursorStore>(sp => string.IsNullOrWhiteSpace(cursorPath)
            ? new NullFederatedEventCursorStore()
            : new FileFederatedEventCursorStore(
                cursorPath, sp.GetRequiredService<ILogger<FileFederatedEventCursorStore>>()));

        services.AddSingleton(sp => new FederatedEventJournalHistory(
            sp.GetRequiredService<IJournalDiscovery>().Discover(),
            budget,
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<ILogger<FederatedEventJournalHistory>>()));

        services.AddSingleton(sp => new FederatedEventSource(
            sp.GetRequiredService<IJournalDiscovery>().Discover(),
            sp.GetRequiredService<IFederatedEventCursorStore>(),
            startPosition,
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<ILogger<FederatedEventSource>>()));

        AddJournalResolution(services);

        return services;
    }

    /// <summary>
    /// Registers how <see cref="IEventSource"/> and <see cref="IEventJournalHistory"/> are decided.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which reader a consumer gets is resolved, not raced.</b> Both
    /// <see cref="AddKgsmJournalReader(IServiceCollection, JournalReadOptions)"/> and
    /// <see cref="AddKgsmJournalFederation"/> register this same rule, and it is registered with
    /// <c>TryAdd</c>, so calling them in either order produces the same container. The rule itself is
    /// the whole point: <b>federated if a federated reader was registered, single-journal
    /// otherwise</b> — a question answered when the container is built, from what is actually in it,
    /// rather than by whichever call happened to come last.
    /// </para>
    /// <para>
    /// The alternative this replaces is worth naming, because it has no symptom. Two valid
    /// <c>AddSingleton</c> registrations of one interface differ only in call order, and a consumer
    /// that federated too early kept reading its single journal **successfully** — reporting a healthy
    /// journal and a quiet host, while the events it wanted sat in four other files. There is no
    /// exception to catch and nothing degrades: one journal is being read correctly.
    /// </para>
    /// <para>
    /// A consumer with a genuine reason to supply its own still can — an explicit registration after
    /// these calls wins by last-registration, exactly as it does for <see cref="IEventCursorStore"/>.
    /// </para>
    /// </remarks>
    private static void AddJournalResolution(IServiceCollection services)
    {
        services.TryAddSingleton<IEventService, EventService>();
        services.TryAddSingleton<IEventSource>(sp =>
            sp.GetService<FederatedEventSource>()
            ?? (IEventSource)sp.GetRequiredService<IEventJournalReader>());

        services.TryAddSingleton<IEventJournalHistory>(sp =>
            sp.GetService<FederatedEventJournalHistory>()
            ?? (IEventJournalHistory)sp.GetRequiredService<EventJournalHistory>());
    }

    private static void TryCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swallowed deliberately, and only here. Failing a component's startup because its audit
            // directory could not be made would take a working service down over its record-keeping,
            // and the writer reports the same problem with a logger once the container exists.
        }
    }
}
