using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Appends v1 envelopes to one producer's journal, one whole line per event.
/// </summary>
/// <remarks>
/// <para>
/// <b>One line, one write.</b> The file is opened in append mode and the entire encoded line —
/// newline included — goes out in a single write call. <c>O_APPEND</c> makes a write below
/// <c>PIPE_BUF</c> atomic, which is what lets any number of writers and readers share a segment with
/// no locking: concurrent appends interleave whole lines and never partial ones. A line longer than
/// <see cref="AtomicWriteLimitBytes"/> loses that guarantee and is logged, because a reader's cursor
/// is a byte offset and a torn line costs more than the event.
/// </para>
/// <para>
/// <b>The envelope is composed by hand</b> rather than serialized from a model. That keeps field
/// order as documented, omits nulls instead of writing them (absent and null mean the same thing to
/// a reader), and needs no new type registered for serialization — the library is reflection-free for
/// its Native-AOT consumers, so a writer that reached for a reflection-based serializer would not
/// survive publishing.
/// </para>
/// <para>
/// <b>Timestamps are millisecond-precision UTC.</b> A single appender gets ordering for free from
/// the file; several journals merged on second granularity would order arbitrarily inside each
/// second, which is exactly where causally adjacent events sit.
/// </para>
/// </remarks>
public sealed class EventJournalWriter : IEventJournalWriter
{
    /// <summary>
    /// The largest line this writer can append atomically. <c>PIPE_BUF</c> on Linux; a single
    /// <c>O_APPEND</c> write up to this size cannot interleave with another writer's.
    /// </summary>
    public const int AtomicWriteLimitBytes = 4096;

    /// <summary>The envelope schema version this writer produces.</summary>
    public const int SchemaVersion = 2;

    /// <summary>
    /// How a <c>Timestamp</c> is spelled: millisecond-precision UTC, <c>Z</c>-suffixed.
    /// </summary>
    /// <remarks>
    /// Public and shared because a format is only a format if the thing that writes it and the thing
    /// that checks it hold the same string. The <c>Z</c> is quoted so the one constant both formats
    /// and round-trips through <c>TryParseExact</c> — unquoted it is a literal to the formatter and a
    /// surprise to the parser.
    /// </remarks>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";

    private readonly EventJournalWriterOptions _options;
    private readonly string _directory;
    private readonly ILogger<EventJournalWriter> _logger;
    private readonly Lock _segmentGate = new();
    private string? _currentSegment;

    /// <summary>
    /// Initializes a writer for the producer named in <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Which producer this is, where its journal lives, and its version.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options are not usable.</exception>
    public EventJournalWriter(EventJournalWriterOptions options, ILogger<EventJournalWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        options.Validate();

        _options = options;
        // Validate() fills Directory from the producer when it was left unset, so it is non-null
        // from here on; held separately so that stays true whatever happens to the options object.
        _directory = options.Directory!;
        _logger = logger;

        // A journal a reader will not attribute to this producer is the one misconfiguration that
        // reports itself as normal operation — the writes succeed and the record is invisible. Said
        // once, at construction, because there is no later moment at which anything notices.
        if (options.DescribeDirectoryMismatch() is { } mismatch)
            _logger.LogWarning("Event journal misconfigured: {Problem}", mismatch);

        EnsureDirectory();

        // Reported after the directory exists, because the mode is a property of the directory rather
        // than of the configuration — a producer can be configured perfectly and still be writing
        // somewhere no other account can enter.
        if (JournalAccess.DescribeUnreachable(_directory) is { } unreachable)
            _logger.LogWarning("Event journal may be unreadable: {Problem}", unreachable);

        // Startup is one of the two moments this producer prunes, and the only one a short-lived
        // process ever reaches: a socket-activated authority may exist for the length of one request,
        // so a timer would never fire and a "prune every N hours" loop has nothing to run in.
        Prune();
    }

    /// <summary>
    /// Removes segments past this producer's retention window.
    /// </summary>
    /// <remarks>
    /// <b>Cadence comes from the data, not from a clock.</b> This runs at startup and again whenever
    /// the segment date rolls over — which is exactly daily for a resident daemon, and is the smallest
    /// unit retention can ever remove, since a segment is a day. So no timer is needed, and with it no
    /// hosting stack: this package is consumed by a root-running firewall authority that builds no
    /// container and by an AOT daemon that counts its megabytes.
    /// <para>
    /// The one case this does not cover: a process that runs for longer than the window <em>and
    /// records nothing in it</em> keeps segments it would otherwise drop. It is also, by construction,
    /// a journal that is not growing — and the next restart prunes it.
    /// </para>
    /// </remarks>
    private void Prune() => JournalRetention.Prune(
        _directory, _options.RetentionDays, _options.Clock?.Invoke() ?? DateTimeOffset.UtcNow, _logger);

    /// <summary>
    /// Prunes when this append is the first to land in a new day's segment.
    /// </summary>
    /// <remarks>
    /// The first write of a process only records which segment it is on; the startup prune has already
    /// run, and pruning twice within a second of each other would be work for nothing. Guarded so the
    /// scan happens once per day rather than once per event.
    /// </remarks>
    private void PruneIfSegmentRolled(string segment)
    {
        lock (_segmentGate)
        {
            if (string.Equals(_currentSegment, segment, StringComparison.Ordinal))
                return;

            bool rolled = _currentSegment is not null;
            _currentSegment = segment;

            if (!rolled)
                return;
        }

        Prune();
    }

    /// <summary>
    /// Creates the journal directory, so this producer is discoverable before it has anything to say.
    /// </summary>
    /// <remarks>
    /// A reader finds a producer by finding its directory, and a consumer scans once when it starts.
    /// Left to the first event, a deployed producer that has not yet had cause to record anything is
    /// indistinguishable from one that writes no journal at all — and stays that way until it emits
    /// <em>and</em> every consumer restarts.
    /// <para>
    /// Failure is not fatal here: the append path creates the directory too, and a permission problem
    /// reported now would be reported again with the event it actually cost.
    /// </para>
    /// </remarks>
    private void EnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "Could not create the journal directory at {Path}; this producer stays undiscoverable "
                + "until it can be written", _directory);
        }
    }

    /// <inheritdoc/>
    public string Producer => _options.Producer;

    /// <inheritdoc/>
    public ValueTask<bool> AppendAsync(
        EventName eventType,
        JsonElement data,
        string? actor = null,
        string? origin = null,
        EventSeverity? severity = null,
        EventOutcome? outcome = null,
        string? summary = null,
        CancellationToken token = default)
    {
        if (eventType.IsEmpty)
            throw new ArgumentException("An event must be named.", nameof(eventType));

        if (data.ValueKind is not JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"An event payload must be a JSON object; got {data.ValueKind}.", nameof(data));
        }

        DateTimeOffset now = _options.Clock?.Invoke() ?? DateTimeOffset.UtcNow;
        byte[] line = Compose(eventType, data, actor, origin, severity, outcome, summary, now);

        if (line.Length > AtomicWriteLimitBytes)
        {
            _logger.LogWarning(
                "Event '{Type}' encodes to {Bytes} bytes, past the {Limit}-byte atomic-append limit; "
                + "a concurrent write to this segment could interleave with it",
                eventType, line.Length, AtomicWriteLimitBytes);
        }

        return new ValueTask<bool>(Append(line, now, eventType.Value, token));
    }

    /// <summary>
    /// Writes the encoded line to the segment for <paramref name="now"/>, creating the directory on
    /// first use.
    /// </summary>
    private bool Append(byte[] line, DateTimeOffset now, string eventType, CancellationToken token)
    {
        string segment = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".ndjson";
        string path = Path.Combine(_directory, segment);

        PruneIfSegmentRolled(segment);

        try
        {
            token.ThrowIfCancellationRequested();

            Directory.CreateDirectory(_directory);

            // FileMode.Append opens with O_APPEND, so the single Write below lands at the end of the
            // file as one operation even with other writers on the same segment. FileShare allows the
            // concurrent readers tailing it, and retention unlinking it underneath.
            using var stream = new FileStream(
                path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 0, useAsync: false);

            stream.Write(line, 0, line.Length);
            stream.Flush();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The action already happened; failing it now because recording it did not work would
            // trade a missing audit line for a broken operation. The caller is told and decides.
            _logger.LogError(ex,
                "Event '{Type}' was NOT recorded: the journal at {Path} could not be written",
                eventType, path);
            return false;
        }
    }

    /// <summary>
    /// Encodes one v1 envelope, compact, with a trailing newline. Null fields are omitted.
    /// </summary>
    private byte[] Compose(
        EventName eventType, JsonElement data, string? actor, string? origin,
        EventSeverity? severity, EventOutcome? outcome, string? summary, DateTimeOffset now)
    {
        var buffer = new ArrayBufferWriter<byte>(512);

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("V", SchemaVersion);

            // The line's own name (reference/event-envelope.md). Minted here and never derived from the content:
            // two identical events in the same second are two events, and a digest folds them into
            // one. UUIDv7 rather than v4 so an id sorts the way the journal does.
            writer.WriteString("Id", Guid.CreateVersion7().ToString("d"));

            writer.WriteString("EventType", eventType.Value);

            writer.WritePropertyName("Data");
            data.WriteTo(writer);

            writer.WriteString(
                "Timestamp",
                now.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));

            // Absent means null to every reader, so an unknown actor or origin is left out rather
            // than written as an explicit null — and never filled in with a plausible substitute.
            if (!string.IsNullOrEmpty(actor))
                writer.WriteString("Actor", actor);

            if (!string.IsNullOrEmpty(origin))
                writer.WriteString("Origin", origin);

            if (!string.IsNullOrEmpty(_options.Hostname))
                writer.WriteString("Hostname", _options.Hostname);

            if (!string.IsNullOrEmpty(_options.ProducerVersion))
                writer.WriteString("ProducerVersion", _options.ProducerVersion);

            // What the producer says about its own event: how much it matters, how it went, and what
            // happened in words. A reader renders an event it has never heard of from these three, so
            // nothing downstream needs a list of event types. Omitted when the producer does not say —
            // absent is unknown, and a default stamped here would be this writer's opinion rather than
            // the producer's knowledge.
            if (severity.HasValue)
                writer.WriteString("Severity", severity.Value.ToWire());

            if (outcome.HasValue)
                writer.WriteString("Outcome", outcome.Value.ToWire());

            if (!string.IsNullOrEmpty(summary))
                writer.WriteString("Summary", summary);

            // OpId / RunId / During are reserved (the correlation work) and nothing populates them
            // yet, so no producer writes them and every reader sees them absent.

            writer.WriteEndObject();
        }

        ReadOnlySpan<byte> json = buffer.WrittenSpan;
        byte[] line = new byte[json.Length + 1];
        json.CopyTo(line);
        line[^1] = (byte)'\n';
        return line;
    }
}
