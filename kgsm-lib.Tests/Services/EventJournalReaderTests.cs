using System.Collections.Concurrent;
using System.Text;
using TheKrystalShip.Testing;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for <see cref="EventJournalReader"/> — the transport that tails the engine's
/// append-only event journal. Unlike the socket client (deliberately out of the unit suite
/// because it needs real socket I/O), the journal is ordinary files, so its whole contract is
/// testable against a temporary directory: where a consumer starts, that only whole lines are
/// delivered, that segments roll, that a position survives a restart, and that a position the
/// journal can no longer satisfy is reported as a gap rather than silently skipped.
/// </summary>
public sealed class EventJournalReaderTests : IDisposable
{
    private readonly string _directory;

    public EventJournalReaderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kgsm-journal-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>A cursor store that keeps the position in memory, standing in for a consumer's own storage.</summary>
    private sealed class MemoryCursorStore : IEventCursorStore
    {
        public EventCursor? Cursor { get; set; }

        public ValueTask<EventCursor?> LoadAsync(CancellationToken token = default)
            => ValueTask.FromResult(Cursor);

        public ValueTask SaveAsync(EventCursor cursor, CancellationToken token = default)
        {
            Cursor = cursor;
            return ValueTask.CompletedTask;
        }
    }

    private EventJournalReader CreateReader(EventStartPosition startPosition, IEventCursorStore cursors)
        => new(
            new KgsmOptions
            {
                KgsmPath = "/usr/local/bin/kgsm",
                EventJournalDirectory = _directory,
                EventStartPosition = startPosition
            },
            cursors,
            new Mock<ILogger<EventJournalReader>>().Object);

    /// <summary>One line of journal, shaped like a real envelope so the payload is representative.</summary>
    private static string Envelope(string instance) =>
        $$"""{"EventType":"server.started","Data":{"InstanceName":"{{instance}}"},"Timestamp":"2026-08-04T10:00:00Z","Actor":"heisen"}""";

    private string SegmentPath(string segment) => Path.Combine(_directory, segment);

    /// <summary>Appends complete lines the way the engine does — one <c>printf >></c> per event.</summary>
    private void Append(string segment, params string[] lines)
    {
        using var stream = new FileStream(SegmentPath(segment), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        foreach (string line in lines)
            writer.Write(line + "\n");
    }

    /// <summary>
    /// Appends exact bytes, the way the machine going down leaves them — a hole carries no newline
    /// of its own, so a writer that added one could not produce the shape this reproduces.
    /// </summary>
    private void AppendRaw(string segment, params byte[][] chunks)
    {
        using var stream = new FileStream(SegmentPath(segment), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        foreach (byte[] chunk in chunks)
            stream.Write(chunk, 0, chunk.Length);
    }

    /// <summary>
    /// Runs a reader for the duration of <paramref name="body"/>, collecting everything it
    /// delivers, and stops it afterwards.
    /// </summary>
    private async Task<ConcurrentQueue<string>> RunAsync(
        EventJournalReader reader,
        Func<ConcurrentQueue<string>, Task> body)
    {
        var received = new ConcurrentQueue<string>();
        reader.EventReceived += (line, _) =>
        {
            received.Enqueue(line);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        Task listening = reader.StartListeningAsync(cts.Token);

        try
        {
            await body(received);
        }
        finally
        {
            cts.Cancel();
            await listening;
            reader.Dispose();
        }

        return received;
    }

    // ── Start position ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tail_SkipsExistingHistoryAndDeliversWhatArrivesAfterwards()
    {
        Append("2026-08-04.ndjson", Envelope("already-here"));

        EventJournalReader reader = CreateReader(EventStartPosition.Tail, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            Append("2026-08-04.ndjson", Envelope("arrived-later"));

            await Eventually.Holds(() => received.Count == 1, "the new event to be delivered");
            Assert.Contains("arrived-later", received.Single());
        });
    }

    [Fact]
    public async Task Oldest_ReplaysEverySurvivingSegmentInChronologicalOrder()
    {
        Append("2026-08-02.ndjson", Envelope("first"));
        Append("2026-08-03.ndjson", Envelope("second"), Envelope("third"));
        Append("2026-08-04.ndjson", Envelope("fourth"));

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 4, "the whole journal to replay");

            string[] order = received.ToArray();
            Assert.Contains("first", order[0]);
            Assert.Contains("second", order[1]);
            Assert.Contains("third", order[2]);
            Assert.Contains("fourth", order[3]);
        });
    }

    /// <summary>
    /// The first thing a consumer reads after the machine comes back is the hole the shutdown left,
    /// with the event that landed against it. That event is delivered.
    /// </summary>
    [Fact]
    public async Task Oldest_DeliversTheEventWrittenAgainstACrashHole()
    {
        AppendRaw("2026-08-04.ndjson",
            Encoding.UTF8.GetBytes(Envelope("before-the-crash") + "\n"),
            new byte[256],
            Encoding.UTF8.GetBytes(Envelope("after-the-crash") + "\n"));

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 2, "both events either side of the hole");

            string[] order = received.ToArray();
            Assert.Contains("before-the-crash", order[0]);
            Assert.Contains("after-the-crash", order[1]);
        });
    }

    [Fact]
    public async Task EmptyJournal_DeliversTheFirstSegmentThatAppears()
    {
        // Nothing has ever been emitted on this host: the directory exists but holds no
        // segments, and the first one to appear is entirely new, so every line in it postdates
        // the reader starting — even under Tail.
        EventJournalReader reader = CreateReader(EventStartPosition.Tail, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            Append("2026-08-04.ndjson", Envelope("the-first-event-ever"));

            await Eventually.Holds(() => received.Count == 1, "the first segment to be picked up");
            Assert.Contains("the-first-event-ever", received.Single());
        });
    }

    [Fact]
    public async Task MissingJournalDirectory_DoesNotFaultTheReader()
    {
        Directory.Delete(_directory, recursive: true);

        EventJournalReader reader = CreateReader(EventStartPosition.Tail, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            // The engine creates the directory on first emit; a consumer that started before
            // that must keep running and pick it up, not crash on a path that isn't there yet.
            Directory.CreateDirectory(_directory);
            Append("2026-08-04.ndjson", Envelope("after-the-directory-appeared"));

            await Eventually.Holds(() => received.Count == 1, "the journal to be picked up once it exists");
            Assert.Contains("after-the-directory-appeared", received.Single());
        });
    }

    // ── Line framing ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PartialLine_IsWithheldUntilItsNewlineArrives()
    {
        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());
        string envelope = Envelope("half-written");

        await RunAsync(reader, async received =>
        {
            // A byte-offset cursor is only exact because a line is all-or-nothing. Delivering a
            // half-flushed append would hand the consumer truncated JSON and leave the offset
            // past bytes it never really read.
            await File.AppendAllTextAsync(SegmentPath("2026-08-04.ndjson"), envelope[..20]);
            await Task.Delay(1500);
            Assert.Empty(received);

            await File.AppendAllTextAsync(SegmentPath("2026-08-04.ndjson"), envelope[20..] + "\n");

            await Eventually.Holds(() => received.Count == 1, "the completed line to be delivered");
            Assert.Equal(envelope, received.Single());
        });
    }

    [Fact]
    public async Task BlankLines_AreNotDelivered()
    {
        await File.WriteAllTextAsync(SegmentPath("2026-08-04.ndjson"), $"\n{Envelope("real")}\n\n");

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 1, "only the real event to be delivered");
            await Task.Delay(500);

            Assert.Single(received);
            Assert.Contains("real", received.Single());
        });
    }

    // ── Segment rolling ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewSegment_IsPickedUpWhenTheDateRollsOver()
    {
        Append("2026-08-04.ndjson", Envelope("yesterday"));

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 1, "the current segment to be read");

            Append("2026-08-05.ndjson", Envelope("today"));

            await Eventually.Holds(() => received.Count == 2, "the new segment to be picked up");
            Assert.Contains("today", received.ToArray()[1]);
        });
    }

    [Fact]
    public async Task LateWriteToTheSegmentLeftBehind_IsStillDelivered()
    {
        Append("2026-08-04.ndjson", Envelope("before-midnight"));

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 1, "the first segment to be read");

            Append("2026-08-05.ndjson", Envelope("after-midnight"));
            await Eventually.Holds(() => received.Count == 2, "the reader to advance to the new segment");

            // The engine names a segment from the clock when the emit starts, so an emit that
            // begins just before midnight lands in the old segment after the reader has already
            // moved on. Skipping it would lose an event to a race with the calendar.
            Append("2026-08-04.ndjson", Envelope("straggler"));

            await Eventually.Holds(() => received.Count == 3, "the late write to the previous segment");
            Assert.Contains("straggler", received.ToArray()[2]);
        });
    }

    // ── Cursors ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cursor_IsStoredPastTheEventsAlreadyDelivered()
    {
        Append("2026-08-04.ndjson", Envelope("one"), Envelope("two"));

        var cursors = new MemoryCursorStore();
        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrOldest, cursors);

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 2, "both events to be delivered");
            await Eventually.Holds(() => cursors.Cursor is not null, "the cursor to be stored");

            Assert.Equal("2026-08-04.ndjson", cursors.Cursor!.Segment);
            Assert.Equal(new FileInfo(SegmentPath("2026-08-04.ndjson")).Length, cursors.Cursor.Offset);
        });
    }

    [Fact]
    public async Task StoredCursor_ResumesWithoutRedeliveringWhatCameBefore()
    {
        Append("2026-08-04.ndjson", Envelope("processed-last-run"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor
            {
                Segment = "2026-08-04.ndjson",
                Offset = new FileInfo(SegmentPath("2026-08-04.ndjson")).Length
            }
        };

        Append("2026-08-04.ndjson", Envelope("missed-while-down"));

        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrTail, cursors);

        await RunAsync(reader, async received =>
        {
            // The point of the journal over a socket: an event emitted while the consumer was
            // down is still there when it comes back.
            await Eventually.Holds(() => received.Count == 1, "the event emitted while down to be delivered");
            Assert.Contains("missed-while-down", received.Single());
        });
    }

    [Fact]
    public async Task StoredCursorInAnOlderSegment_ReadsForwardThroughEverySegmentSince()
    {
        Append("2026-08-02.ndjson", Envelope("read-last-run"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor
            {
                Segment = "2026-08-02.ndjson",
                Offset = new FileInfo(SegmentPath("2026-08-02.ndjson")).Length
            }
        };

        Append("2026-08-03.ndjson", Envelope("day-two"));
        Append("2026-08-04.ndjson", Envelope("day-three"));

        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrTail, cursors);

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 2, "both later segments to be caught up");

            string[] order = received.ToArray();
            Assert.Contains("day-two", order[0]);
            Assert.Contains("day-three", order[1]);
        });
    }

    [Fact]
    public async Task TailIgnoresAStoredCursor()
    {
        Append("2026-08-04.ndjson", Envelope("history"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor { Segment = "2026-08-04.ndjson", Offset = 0 }
        };

        EventJournalReader reader = CreateReader(EventStartPosition.Tail, cursors);

        await RunAsync(reader, async received =>
        {
            Append("2026-08-04.ndjson", Envelope("live"));

            await Eventually.Holds(() => received.Count == 1, "only the live event to be delivered");
            Assert.Contains("live", received.Single());
        });
    }

    // ── Gaps ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrunedSegment_ReportsAGapAndFallsBackToTheColdStartPosition()
    {
        // Retention deletes segments on age alone and never consults a consumer, so a consumer
        // absent longer than the retention window finds its position gone. What must not happen
        // is resuming quietly, which would present a partial history as a whole one.
        Append("2026-08-04.ndjson", Envelope("survived"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor { Segment = "2026-05-01.ndjson", Offset = 512 }
        };

        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrOldest, cursors);

        EventJournalGap? gap = null;
        reader.GapDetected += reported =>
        {
            gap = reported;
            return Task.CompletedTask;
        };

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => gap is not null, "the gap to be reported");

            Assert.Equal("2026-05-01.ndjson", gap!.LostSegment);
            Assert.Equal(512, gap.LostOffset);
            Assert.Equal(EventJournalGapReason.SegmentPruned, gap.Reason);
            Assert.Equal("2026-08-04.ndjson", gap.ResumedAtSegment);
            Assert.Equal(0, gap.ResumedAtOffset);

            await Eventually.Holds(() => received.Count == 1, "reading to resume at the oldest surviving segment");
            Assert.Contains("survived", received.Single());
        });
    }

    [Fact]
    public async Task TruncatedSegment_ReportsAGapRatherThanReadingTheWrongBytes()
    {
        Append("2026-08-04.ndjson", Envelope("rewritten"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor
            {
                Segment = "2026-08-04.ndjson",
                Offset = new FileInfo(SegmentPath("2026-08-04.ndjson")).Length + 4096
            }
        };

        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrTail, cursors);

        EventJournalGap? gap = null;
        reader.GapDetected += reported =>
        {
            gap = reported;
            return Task.CompletedTask;
        };

        await RunAsync(reader, async _ =>
        {
            await Eventually.Holds(() => gap is not null, "the gap to be reported");
            Assert.Equal(EventJournalGapReason.SegmentTruncated, gap!.Reason);
        });
    }

    [Fact]
    public async Task NoGapIsReportedWhenTheCursorIsStillValid()
    {
        Append("2026-08-04.ndjson", Envelope("one"));

        var cursors = new MemoryCursorStore
        {
            Cursor = new EventCursor { Segment = "2026-08-04.ndjson", Offset = 0 }
        };

        EventJournalReader reader = CreateReader(EventStartPosition.CursorOrTail, cursors);

        bool reported = false;
        reader.GapDetected += _ =>
        {
            reported = true;
            return Task.CompletedTask;
        };

        await RunAsync(reader, async received =>
        {
            await Eventually.Holds(() => received.Count == 1, "the event to be delivered from the cursor");
            Assert.False(reported, "a satisfiable cursor must not be reported as a gap");
        });
    }

    // ── Robustness ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AThrowingSubscriberDoesNotStopTheStream()
    {
        Append("2026-08-04.ndjson", Envelope("poison"));

        EventJournalReader reader = CreateReader(EventStartPosition.Oldest, new MemoryCursorStore());

        var seen = new ConcurrentQueue<string>();
        reader.EventReceived += (line, _) =>
        {
            seen.Enqueue(line);
            throw new InvalidOperationException("handler blew up");
        };

        using var cts = new CancellationTokenSource();
        Task listening = reader.StartListeningAsync(cts.Token);

        try
        {
            await Eventually.Holds(() => seen.Count == 1, "the first event to reach the handler");

            Append("2026-08-04.ndjson", Envelope("still-flowing"));

            await Eventually.Holds(() => seen.Count == 2, "the stream to continue past the throwing handler");
        }
        finally
        {
            cts.Cancel();
            await listening;
            reader.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithoutAJournalDirectory_Throws()
    {
        var options = new KgsmOptions
        {
            KgsmPath = "/usr/local/bin/kgsm",
            EventJournalDirectory = string.Empty
        };

        Assert.Throws<ArgumentException>(() =>
            new EventJournalReader(options, new MemoryCursorStore(), new Mock<ILogger<EventJournalReader>>().Object));
    }
}
