using System.Text;
using Microsoft.Extensions.Logging;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Services;

/// <summary>
/// Tails the KGSM event journal, emitting one <see cref="IEventSource.EventReceived"/> per
/// line as the engine appends them.
/// </summary>
/// <remarks>
/// <para>
/// The journal is a directory of date-named NDJSON segments (<c>YYYY-MM-DD.ndjson</c>) whose
/// names sort lexically in chronological order, so replay is glob order with no merge step.
/// Position is a <see cref="EventCursor"/> — a segment plus a byte offset — which is exact
/// only because the engine writes each event as a single compact line and retention deletes
/// whole segments rather than truncating them.
/// </para>
/// <para>
/// Only complete lines are dispatched. A read that ends mid-line leaves the offset before it,
/// so a partially-flushed append is picked up whole on the next pass rather than delivered
/// truncated.
/// </para>
/// </remarks>
public sealed class EventJournalReader : IEventJournalReader
{
    /// <summary>Journal segments are the only files the reader considers.</summary>
    private const string SegmentPattern = "*.ndjson";

    /// <summary>
    /// Backstop poll interval. The filesystem watcher supplies the low-latency path; this
    /// bounds latency when the watcher is unavailable (it needs the directory to exist) or
    /// misses a notification, and drives the straggler re-check below.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a segment the reader has moved past stays eligible for re-reading.
    /// <para>
    /// The engine picks a segment name from the clock at emit time, so an emit that starts
    /// just before midnight can append to yesterday's segment after the reader has already
    /// advanced into today's. Re-checking the segment left behind for a grace period is what
    /// stops that write from being skipped; two minutes covers the slowest emit by orders of
    /// magnitude.
    /// </para>
    /// </summary>
    private static readonly TimeSpan StragglerGrace = TimeSpan.FromMinutes(2);

    private const int ReadBufferSize = 64 * 1024;

    private readonly string _directory;
    private readonly IEventCursorStore _cursors;
    private readonly ILogger<EventJournalReader> _logger;

    /// <summary>The segment currently being read, or null when the journal holds none yet.</summary>
    private string? _segment;

    /// <summary>The offset of the first unread byte in <see cref="_segment"/>.</summary>
    private long _offset;

    /// <summary>
    /// Segments the reader has advanced past that are still inside their grace window, keyed
    /// by segment name, each with the offset it was left at and when it stops being watched.
    /// Normally empty, and never more than one entry outside a clock jump.
    /// </summary>
    private readonly Dictionary<string, StragglerState> _stragglers = [];

    private bool _disposed;

    /// <inheritdoc/>
    public event Func<string, EventPosition, Task>? EventReceived;

    /// <inheritdoc/>
    public event Func<EventJournalGap, Task>? GapDetected;

    /// <inheritdoc/>
    public string JournalDirectory => _directory;

    /// <inheritdoc/>
    public EventStartPosition StartPosition { get; set; }

    /// <summary>
    /// Initializes a new reader over the journal directory named by <paramref name="options"/>.
    /// </summary>
    /// <param name="options">KGSM options supplying the journal directory and start position.</param>
    /// <param name="cursors">Where this consumer's position is stored between runs.</param>
    /// <param name="logger">The logger to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the options carry no journal directory.</exception>
    public EventJournalReader(JournalReadOptions options, IEventCursorStore cursors, ILogger<EventJournalReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(cursors, nameof(cursors));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        if (string.IsNullOrWhiteSpace(options.EventJournalDirectory))
            throw new ArgumentException("Event journal directory cannot be null, empty, or whitespace.", nameof(options));

        _directory = options.EventJournalDirectory;
        _cursors = cursors;
        _logger = logger;
        StartPosition = options.EventStartPosition;

        _logger.LogDebug("EventJournalReader initialized on {Directory}", _directory);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    /// <inheritdoc/>
    public async Task StartListeningAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EventJournalReader));

        _logger.LogInformation(
            "Reading the KGSM event journal at {Directory} (start position: {StartPosition})",
            _directory, StartPosition);

        await ResolveStartPositionAsync(token).ConfigureAwait(false);

        // The watcher only wakes the loop early; the loop re-reads the directory itself, so a
        // missed or coalesced notification costs latency (bounded by PollInterval), never an
        // event.
        using var wake = new SemaphoreSlim(0, 1);
        FileSystemWatcher? watcher = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                watcher ??= TryCreateWatcher(wake);

                try
                {
                    await PumpAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A transient filesystem failure must not end the reader: the next pass
                    // retries from the same cursor, so nothing is skipped.
                    _logger.LogError(ex, "Error reading the event journal at {Directory}", _directory);
                }

                try
                {
                    await wake.WaitAsync(PollInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            watcher?.Dispose();
        }

        _logger.LogInformation("Event journal reader stopped");
    }

    /// <summary>
    /// Creates a filesystem watcher that releases <paramref name="wake"/> when the journal
    /// directory changes, or returns null while the directory does not yet exist (a host that
    /// has not emitted its first event) — the poll interval covers that case until it appears.
    /// </summary>
    private FileSystemWatcher? TryCreateWatcher(SemaphoreSlim wake)
    {
        if (!Directory.Exists(_directory))
            return null;

        try
        {
            var watcher = new FileSystemWatcher(_directory, SegmentPattern)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false
            };

            void Signal(object? sender, FileSystemEventArgs e)
            {
                // The semaphore caps at one permit: many appends between passes collapse into
                // a single wake-up, which is what the loop wants.
                try
                {
                    wake.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Already signalled — the pending pass will see this write too.
                }
                catch (ObjectDisposedException)
                {
                    // The loop has exited.
                }
            }

            watcher.Created += Signal;
            watcher.Changed += Signal;
            watcher.Renamed += (_, _) => Signal(null, null!);
            watcher.EnableRaisingEvents = true;

            return watcher;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not watch {Directory} for changes — falling back to polling every {Seconds}s",
                _directory, PollInterval.TotalSeconds);
            return null;
        }
    }

    /// <summary>
    /// Reads everything available: the current segment to its end, then each later segment in
    /// turn, plus any segment still inside its straggler grace window. Saves the resulting
    /// position once, at the end.
    /// </summary>
    private async Task PumpAsync(CancellationToken token)
    {
        bool advanced = await DrainStragglersAsync(token).ConfigureAwait(false);

        while (!token.IsCancellationRequested)
        {
            if (_segment is null)
            {
                // The journal was empty when the reader started. The first segment to appear
                // is read whole regardless of start position: it did not exist at start time,
                // so every line in it was appended after this consumer began listening.
                _segment = ListSegments().FirstOrDefault();
                if (_segment is null)
                    break;

                _offset = 0;
                _logger.LogDebug("Event journal segment {Segment} appeared", _segment);
            }

            (DrainOutcome outcome, long offset) = await DrainAsync(_segment, _offset, token).ConfigureAwait(false);

            if (offset != _offset)
            {
                _offset = offset;
                advanced = true;
            }

            if (outcome == DrainOutcome.Truncated)
            {
                await ReportGapAsync(
                    new EventCursor { Segment = _segment, Offset = _offset },
                    EventJournalGapReason.SegmentTruncated,
                    ListSegments()).ConfigureAwait(false);
                return;
            }

            string? next = NextSegmentAfter(_segment);
            if (next is null)
                break;

            _stragglers[_segment] = new StragglerState(_offset, DateTimeOffset.UtcNow + StragglerGrace);
            _logger.LogDebug("Advancing the event journal from {Segment} to {Next}", _segment, next);

            _segment = next;
            _offset = 0;
            advanced = true;
        }

        if (advanced && _segment is not null)
            await SaveCursorAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-reads segments the reader has moved past but whose grace window has not expired,
    /// picking up a write that landed after the advance (see <see cref="StragglerGrace"/>).
    /// </summary>
    /// <returns>True if any straggler produced new bytes.</returns>
    private async Task<bool> DrainStragglersAsync(CancellationToken token)
    {
        if (_stragglers.Count == 0)
            return false;

        bool advanced = false;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (string segment in _stragglers.Keys.ToArray())
        {
            StragglerState state = _stragglers[segment];

            (DrainOutcome outcome, long offset) = await DrainAsync(segment, state.Offset, token).ConfigureAwait(false);

            if (offset != state.Offset)
            {
                _logger.LogWarning(
                    "Event journal segment {Segment} received {Bytes} bytes after the reader had moved past it — a write that straddled the segment boundary",
                    segment, offset - state.Offset);
                advanced = true;
            }

            if (outcome != DrainOutcome.Ok || now >= state.WatchUntil)
                _stragglers.Remove(segment);
            else
                _stragglers[segment] = state with { Offset = offset };
        }

        return advanced;
    }

    /// <summary>
    /// Stores the position to resume from.
    /// </summary>
    /// <remarks>
    /// The saved position is the OLDEST unfinished one — a straggler still inside its grace
    /// window, if there is one, rather than the segment being read now. Saving the newer
    /// position would make a restart skip past a late boundary write for good; saving the
    /// older one costs re-delivery of events already dispatched, which an idempotent consumer
    /// absorbs. Loss is not recoverable, duplication is.
    /// </remarks>
    private async ValueTask SaveCursorAsync(CancellationToken token)
    {
        string segment = _segment!;
        long offset = _offset;

        foreach ((string candidate, StragglerState state) in _stragglers)
        {
            if (string.CompareOrdinal(candidate, segment) < 0)
            {
                segment = candidate;
                offset = state.Offset;
            }
        }

        try
        {
            await _cursors.SaveAsync(new EventCursor { Segment = segment, Offset = offset }, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A cursor that fails to store costs re-delivery on the next start, never loss, so
            // it is reported and the read loop continues.
            _logger.LogError(ex, "Failed to store the event journal cursor at {Segment}+{Offset}", segment, offset);
        }
    }

    private enum DrainOutcome
    {
        /// <summary>Read to the end of what is currently in the segment.</summary>
        Ok,

        /// <summary>The segment is gone.</summary>
        Missing,

        /// <summary>The segment is shorter than the offset — it was rewritten, not appended to.</summary>
        Truncated
    }

    /// <summary>
    /// Dispatches every complete line in <paramref name="segment"/> from
    /// <paramref name="offset"/> to the current end of the file.
    /// </summary>
    /// <returns>
    /// The outcome, and the offset just past the last complete line dispatched — unchanged
    /// from <paramref name="offset"/> when there was nothing new or only a partial line.
    /// </returns>
    private async Task<(DrainOutcome Outcome, long Offset)> DrainAsync(string segment, long offset, CancellationToken token)
    {
        string path = Path.Combine(_directory, segment);

        FileStream stream;
        try
        {
            // FileShare.ReadWrite|Delete: the engine appends to this file while it is open,
            // and retention may unlink it underneath the reader. Neither is an error here.
            stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadBufferSize, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            return (DrainOutcome.Missing, offset);
        }
        catch (DirectoryNotFoundException)
        {
            return (DrainOutcome.Missing, offset);
        }

        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length < offset)
                return (DrainOutcome.Truncated, offset);

            if (stream.Length == offset)
                return (DrainOutcome.Ok, offset);

            stream.Seek(offset, SeekOrigin.Begin);

            byte[] buffer = new byte[ReadBufferSize];
            using var partial = new MemoryStream();

            long consumed = offset;        // just past the last complete line
            long bufferStart = offset;     // file offset of buffer[0]
            int read;

            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
            {
                int start = 0;

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                        continue;

                    partial.Write(buffer, start, i - start);

                    // `consumed` sits just past the previous line's newline, which is exactly
                    // where this line begins — captured before it advances, because that offset
                    // is the event's identity for as long as the segment exists.
                    long lineStart = consumed;
                    consumed = bufferStart + i + 1;
                    start = i + 1;

                    // A reader resuming after an unclean shutdown meets the hole the filesystem left
                    // where the last appends were going to be; the event past it landed and is
                    // dispatched, while the offset stays the one the line occupies on disk.
                    string line = JournalLine.WithoutHole(
                        Encoding.UTF8
                            .GetString(partial.GetBuffer(), 0, (int)partial.Length)
                            .Trim());
                    partial.SetLength(0);

                    if (line.Length > 0)
                        await EmitAsync(line, new EventPosition(segment, lineStart)).ConfigureAwait(false);
                }

                // Whatever follows the last newline is an incomplete line. It stays out of
                // `consumed`, so the next pass re-reads it once the writer finishes it.
                partial.Write(buffer, start, read - start);
                bufferStart += read;
            }

            return (DrainOutcome.Ok, consumed);
        }
    }

    /// <summary>
    /// Hands one raw envelope, and where it sits, to the subscriber. A throwing subscriber is
    /// logged and the read continues: one bad event never stalls the journal.
    /// </summary>
    private async Task EmitAsync(string line, EventPosition position)
    {
        Func<string, EventPosition, Task>? handler = EventReceived;
        if (handler is null)
            return;

        try
        {
            await handler(line, position).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching an event from the journal");
        }
    }

    /// <summary>
    /// Picks the position to start reading from, honouring the stored cursor where the start
    /// position asks for it and the journal can still satisfy it.
    /// </summary>
    private async Task ResolveStartPositionAsync(CancellationToken token)
    {
        IReadOnlyList<string> segments = ListSegments();

        if (StartPosition is EventStartPosition.CursorOrTail or EventStartPosition.CursorOrOldest)
        {
            EventCursor? saved = null;
            try
            {
                saved = await _cursors.LoadAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // An unreadable cursor is a cold start, not a crash — but never a silent one.
                _logger.LogError(ex, "Failed to load the stored event journal cursor — starting cold");
            }

            if (saved is not null && !string.IsNullOrEmpty(saved.Segment))
            {
                string path = Path.Combine(_directory, saved.Segment);

                if (!File.Exists(path))
                {
                    await ReportGapAsync(saved, EventJournalGapReason.SegmentPruned, segments).ConfigureAwait(false);
                    return;
                }

                if (saved.Offset > new FileInfo(path).Length)
                {
                    await ReportGapAsync(saved, EventJournalGapReason.SegmentTruncated, segments).ConfigureAwait(false);
                    return;
                }

                _segment = saved.Segment;
                _offset = saved.Offset;

                _logger.LogInformation("Resuming the event journal at {Segment}+{Offset}", _segment, _offset);
                return;
            }
        }

        ColdStart(segments);

        _logger.LogInformation(
            "Starting the event journal at {Segment}+{Offset}",
            _segment ?? "(no segments yet)", _offset);
    }

    /// <summary>
    /// Positions the reader with no cursor to go on: at the oldest surviving segment for a
    /// consumer that replays, or at the end of the newest for one that only wants what
    /// happens from now on.
    /// </summary>
    private void ColdStart(IReadOnlyList<string> segments)
    {
        _stragglers.Clear();

        if (segments.Count == 0)
        {
            _segment = null;
            _offset = 0;
            return;
        }

        if (StartPosition is EventStartPosition.Oldest or EventStartPosition.CursorOrOldest)
        {
            _segment = segments[0];
            _offset = 0;
            return;
        }

        _segment = segments[^1];
        _offset = SegmentLength(_segment);
    }

    /// <summary>
    /// Announces that a stored position could not be honoured, then falls back to the cold-start
    /// position. The events between the two are gone from this consumer's view and are reported
    /// as such rather than quietly skipped.
    /// </summary>
    private async Task ReportGapAsync(EventCursor lost, EventJournalGapReason reason, IReadOnlyList<string> segments)
    {
        ColdStart(segments);

        var gap = new EventJournalGap(
            lost.Segment, lost.Offset, reason,
            _segment, _offset, DateTimeOffset.UtcNow);

        _logger.LogWarning(
            "Event journal gap: the position {LostSegment}+{LostOffset} is unusable ({Reason}); resuming at {Segment}+{Offset}. Events between those points were never seen by this consumer and cannot be recovered.",
            gap.LostSegment, gap.LostOffset, gap.Reason, gap.ResumedAtSegment ?? "(no segments)", gap.ResumedAtOffset);

        Func<EventJournalGap, Task>? handler = GapDetected;
        if (handler is null)
            return;

        try
        {
            await handler(gap).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in the event journal gap handler");
        }
    }

    /// <summary>
    /// The journal's segment file names, oldest first. Names are dates, so ordinal sort order
    /// is chronological order.
    /// </summary>
    private IReadOnlyList<string> ListSegments()
    {
        if (!Directory.Exists(_directory))
            return [];

        try
        {
            string[] paths = Directory.GetFiles(_directory, SegmentPattern);
            string[] names = new string[paths.Length];

            for (int i = 0; i < paths.Length; i++)
                names[i] = Path.GetFileName(paths[i]);

            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to list event journal segments in {Directory}", _directory);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Not permitted to read the event journal directory {Directory}", _directory);
            return [];
        }
    }

    /// <summary>The first segment chronologically after <paramref name="segment"/>, if any.</summary>
    private string? NextSegmentAfter(string segment)
    {
        foreach (string candidate in ListSegments())
        {
            if (string.CompareOrdinal(candidate, segment) > 0)
                return candidate;
        }

        return null;
    }

    private long SegmentLength(string segment)
    {
        try
        {
            return new FileInfo(Path.Combine(_directory, segment)).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>A segment the reader has advanced past, still eligible for a late append.</summary>
    private readonly record struct StragglerState(long Offset, DateTimeOffset WatchUntil);
}
