using System.Globalization;
using System.Text;

using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Tests for <see cref="EventJournalHistory"/> — reading back over the engine's event journal.
/// Like the tailing reader's suite these run the real thing against a temporary directory,
/// because the journal is ordinary files: every filter, the ordering, the keyset cursor across
/// a segment boundary, what the reader will and will not claim about coverage, and each way a
/// journal can be unreadable are all exercised end to end with no fake in the middle.
/// </summary>
public sealed class EventJournalHistoryTests : IDisposable
{
    private readonly string _directory;

    public EventJournalHistoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kgsm-history-tests", Path.GetRandomFileName());
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

    /// <summary>
    /// A line written under a name that has since been renamed reads back under the current one.
    /// </summary>
    /// <remarks>
    /// Retention holds a segment for months, so a reader meets both spellings for a whole retention
    /// period after a producer cuts over. Resolving it where the line is read is what keeps every
    /// consumer above working in one vocabulary — the alternative is each of them carrying a second
    /// name for the same event, which is the drift the single vocabulary exists to remove.
    /// </remarks>
    [Fact]
    public async Task QueryAsync_ALineWrittenUnderTheOldNameReadsBackUnderTheCurrentOne()
    {
        Append("2026-08-16.ndjson", Named("instance_ready", "2026-08-16T10:00:00.000Z", "a", "Ketchup"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        EventHistoryEntry entry = Assert.Single(page.Events);
        Assert.Equal("server.ready", entry.Type);
    }

    /// <summary>A name nothing has renamed is read back exactly as it was written.</summary>
    [Fact]
    public async Task QueryAsync_ANameNothingHasRenamedIsUntouched()
    {
        Append("2026-08-16.ndjson", Named("server.ready", "2026-08-16T10:00:00.000Z", "a", "Ketchup"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("server.ready", Assert.Single(page.Events).Type);
    }

    private EventJournalHistory CreateHistory(
        long? budgetBytes = null, Mock<ILogger<EventJournalHistory>>? logger = null)
        => new(
            new JournalReadOptions
            {
                EventJournalDirectory = _directory,
                EventHistoryScanBudgetBytes = budgetBytes ?? JournalReadOptions.DefaultEventHistoryScanBudgetBytes
            },
            (logger ?? new Mock<ILogger<EventJournalHistory>>()).Object);

    /// <summary>One line of journal, shaped like a real envelope.</summary>
    private static string Envelope(
        string type, string timestamp, string? instance = null, string? blueprint = null, string? actor = null)
    {
        string data = (instance, blueprint) switch
        {
            (not null, _) => $$"""{"InstanceName":"{{instance}}"}""",
            (_, not null) => $$"""{"BlueprintName":"{{blueprint}}"}""",
            _ => "{}"
        };

        string actorField = actor is null ? "" : $$""","Actor":"{{actor}}" """.TrimEnd();
        return $$"""{"EventType":"{{type}}","Data":{{data}},"Timestamp":"{{timestamp}}"{{actorField}}}""";
    }

    /// <summary>The same envelope, carrying the id its producer minted for it.</summary>
    private static string Named(string type, string timestamp, string id, string? instance = null) =>
        Envelope(type, timestamp, instance).Insert(1, $"\"Id\":\"{id}\",");

    private static string Uuid7(int n) => $"01a016e9-d535-7b03-8a6a-{n:d12}";

    /// <summary>Appends complete lines the way the engine does — one whole line per event.</summary>
    private void Append(string segment, params string[] lines)
    {
        using var stream = new FileStream(
            Path.Combine(_directory, segment), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        foreach (string line in lines)
            writer.Write(line + "\n");
    }

    /// <summary>
    /// Appends exact bytes, the way a crash leaves them — a hole carries no newline of its own, so
    /// a writer that adds one could not produce the shape this reproduces.
    /// </summary>
    private void AppendRaw(string segment, params byte[][] chunks)
    {
        using var stream = new FileStream(
            Path.Combine(_directory, segment), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        foreach (byte[] chunk in chunks)
            stream.Write(chunk, 0, chunk.Length);
    }

    // ── Filters ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsEverythingNewestFirst()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            Envelope("server.stopped", "2026-08-04T11:00:00Z", instance: "factorio"));
        Append("2026-08-05.ndjson",
            Envelope("server.started", "2026-08-05T09:00:00Z", instance: "terraria"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.True(page.JournalReadable);
        Assert.Equal(3, page.Events.Count);
        Assert.Equal(
            ["2026-08-05T09:00:00Z", "2026-08-04T11:00:00Z", "2026-08-04T10:00:00Z"],
            page.Events.Select(e => e.Ts.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
    }

    [Fact]
    public async Task QueryAsync_InstanceFilter_ReturnsOnlyThatInstance()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T10:05:00Z", instance: "terraria"));

        EventHistoryPage page = await CreateHistory()
            .QueryAsync(new EventHistoryQuery { Instance = "factorio" });

        Assert.Equal("factorio", Assert.Single(page.Events).Instance);
    }

    [Fact]
    public async Task QueryAsync_TypeFilter_ReturnsOnlyThatType()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory()
            .QueryAsync(new EventHistoryQuery { Type = "server.stopped" });

        Assert.Equal("server.stopped", Assert.Single(page.Events).Type);
    }

    /// <summary>
    /// The two scopes are orthogonal columns, not one backing the other. A server and the
    /// blueprint it was built from routinely share a name, and conflating them would answer
    /// "what happened to this server" with a file edit.
    /// </summary>
    [Fact]
    public async Task QueryAsync_InstanceAndBlueprintOfTheSameName_NeverCrossOver()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            Envelope("blueprint_written", "2026-08-04T10:05:00Z", blueprint: "factorio"));

        EventJournalHistory history = CreateHistory();

        EventHistoryPage byInstance = await history.QueryAsync(new EventHistoryQuery { Instance = "factorio" });
        EventHistoryPage byBlueprint = await history.QueryAsync(new EventHistoryQuery { Blueprint = "factorio" });

        Assert.Equal("server.started", Assert.Single(byInstance.Events).Type);
        Assert.Null(Assert.Single(byInstance.Events).Blueprint);

        Assert.Equal("blueprint_written", Assert.Single(byBlueprint.Events).Type);
        Assert.Null(Assert.Single(byBlueprint.Events).Instance);
    }

    [Fact]
    public async Task QueryAsync_TimeWindow_ExcludesEventsOutsideIt()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T09:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T12:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T15:00:00Z", instance: "factorio"));

        long since = DateTimeOffset.Parse("2026-08-04T11:00:00Z").ToUnixTimeMilliseconds();
        long until = DateTimeOffset.Parse("2026-08-04T13:00:00Z").ToUnixTimeMilliseconds();

        EventHistoryPage page = await CreateHistory()
            .QueryAsync(new EventHistoryQuery { SinceMs = since, UntilMs = until });

        Assert.Equal(
            "2026-08-04T12:00:00Z",
            Assert.Single(page.Events).Ts.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    /// <summary>
    /// The window prunes candidate segments by file name before anything is opened. A day fully
    /// outside it must not contribute, and a day inside it must — the pruning is an optimization
    /// and is only correct if it never changes the answer.
    /// </summary>
    [Fact]
    public async Task QueryAsync_TimeWindow_SpanningSegments_ReadsOnlyWhatItShould()
    {
        Append("2026-07-01.ndjson", Envelope("server.started", "2026-07-01T10:00:00Z", instance: "old"));
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "wanted"));
        Append("2026-09-01.ndjson", Envelope("server.started", "2026-09-01T10:00:00Z", instance: "new"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery
        {
            SinceMs = DateTimeOffset.Parse("2026-08-04T00:00:00Z").ToUnixTimeMilliseconds(),
            UntilMs = DateTimeOffset.Parse("2026-08-04T23:59:59Z").ToUnixTimeMilliseconds()
        });

        Assert.Equal("wanted", Assert.Single(page.Events).Instance);
    }

    // ── Identity and ordering ────────────────────────────────────────────────────────────

    /// <summary>
    /// The regression the position-derived id exists for. A content hash over a one-second
    /// timestamp gives two identical events in the same second one id, and whichever consumer
    /// keys on it drops the second as a duplicate — losing a real occurrence.
    /// </summary>
    [Fact]
    public async Task QueryAsync_IdenticalEventsInTheSameSecond_AreBothReturnedWithDistinctIds()
    {
        string identical = Envelope("player.joined", "2026-08-04T10:00:00Z", instance: "factorio");
        Append("2026-08-04.ndjson", identical, identical, identical);

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal(3, page.Events.Count);
        Assert.Equal(3, page.Events.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task QueryAsync_Id_IsTheLinesOwnNameWhenItHasOne()
    {
        // A position is right only while a segment is appended to and deleted whole. Delete one line
        // and every id after it silently becomes the id of a DIFFERENT event — the row keeps its
        // identity here instead, and the rewrite shows up as a position that no longer resolves.
        Append("2026-08-04.ndjson",
            Named("server.started", "2026-08-04T10:00:00Z", Uuid7(1), instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("evt_" + Uuid7(1), Assert.Single(page.Events).Id);
    }

    [Fact]
    public async Task QueryAsync_Id_StaysPositionalForALineWithNoName()
    {
        // Every line written before the field existed is on disk for as long as retention holds it,
        // and each one still needs an id. Falling back is what keeps the back catalogue addressable.
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("evt_2026-08-04_000000000000", Assert.Single(page.Events).Id);
    }

    [Fact]
    public async Task QueryAsync_Id_FallsBackWhenTheLinesNameIsMalformed()
    {
        // An id this ecosystem did not write cannot be assumed unique or ordered, and building an audit
        // id on one would put a duplicate or a mis-sort into the page. The shape is checked, not
        // trusted; envelope.event-id-shape is where the producer gets told.
        Append("2026-08-04.ndjson",
            Named("server.started", "2026-08-04T10:00:00Z", "NOT-A-UUID", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("evt_2026-08-04_000000000000", Assert.Single(page.Events).Id);
    }

    /// <summary>
    /// Paging must not skip a row when the id scheme changes underneath a cursor — which is what a
    /// deploy does to a client mid-scroll.
    /// </summary>
    /// <remarks>
    /// The cursor is <c>(timestamp, id)</c> and the id is only the tie-break <em>within one
    /// millisecond</em>, so this is the only place a scheme change can be felt. The check is that the
    /// walk loses nothing: a duplicate is a re-render, where a skipped audit row is a fact nobody
    /// ever sees.
    /// </remarks>
    [Fact]
    public async Task QueryAsync_ACursorFromTheOldSchemeSkipsNothing()
    {
        const string ts = "2026-08-04T10:00:00.000Z";

        // Four events sharing one millisecond — the tie-break's whole domain.
        Append("2026-08-04.ndjson",
            Named("server.started", ts, Uuid7(1), instance: "a"),
            Named("server.ready", ts, Uuid7(2), instance: "b"),
            Named("server.stopped", ts, Uuid7(3), instance: "c"),
            Named("server.started", ts, Uuid7(4), instance: "d"));

        IEventJournalHistory history = CreateHistory();

        EventHistoryPage all = await history.QueryAsync(new EventHistoryQuery());
        Assert.Equal(4, all.Events.Count);

        // A cursor the PREVIOUS build would have handed out, naming the second line by its position.
        // Byte offsets are what that scheme encoded, and the second line starts after the first.
        long secondOffset = Encoding.UTF8.GetByteCount(
            Named("server.started", ts, Uuid7(1), instance: "a")) + 1;

        EventHistoryPage next = await history.QueryAsync(new EventHistoryQuery
        {
            BeforeTsMs = DateTimeOffset.Parse(ts, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(),
            BeforeId = AuditId.ForPosition("2026-08-04", secondOffset),
        });

        // Nothing the old cursor named is lost: every event still reachable from it.
        Assert.Equal(
            all.Events.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal),
            next.Events.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// A page of mixed named and unnamed lines pages cleanly, which is what the whole retention window
    /// looks like until every line predating the id has aged out.
    /// </summary>
    [Fact]
    public async Task QueryAsync_MixedNamedAndUnnamedLinesPageWithoutLoss()
    {
        const string ts = "2026-08-04T10:00:00.000Z";

        Append("2026-08-04.ndjson",
            Named("server.started", ts, Uuid7(1), instance: "a"),
            Envelope("server.ready", ts, "b"),
            Named("server.stopped", ts, Uuid7(3), instance: "c"),
            Envelope("server.started", ts, "d"));

        IEventJournalHistory history = CreateHistory();

        var seen = new List<string>();
        long? cursorTs = null;
        string? cursorId = null;

        // Walk one row at a time, the way a client does.
        for (int page = 0; page < 8; page++)
        {
            EventHistoryPage p = await history.QueryAsync(new EventHistoryQuery
            {
                Limit = 1, BeforeTsMs = cursorTs, BeforeId = cursorId,
            });

            if (p.Events.Count == 0) break;

            seen.AddRange(p.Events.Select(e => e.Id));
            if (p.NextCursorTsMs is null) break;

            cursorTs = p.NextCursorTsMs;
            cursorId = p.NextCursorId;
        }

        // Four rows, each exactly once: no skip, and no row served twice.
        Assert.Equal(4, seen.Count);
        Assert.Equal(4, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task QueryAsync_Id_EncodesTheJournalPosition()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        string id = Assert.Single(page.Events).Id;
        Assert.True(AuditId.TryParsePosition(id, out string segment, out long offset));
        Assert.Equal("2026-08-04", segment);
        Assert.Equal(0, offset);
    }

    /// <summary>
    /// Ids sort like the file they came from, which is what lets one value be both identity and
    /// cursor — the caller compares ids as plain strings and gets journal order.
    /// </summary>
    [Fact]
    public async Task QueryAsync_Ids_SortInJournalOrderAsPlainStrings()
    {
        Append("2026-08-04.ndjson", Enumerable.Range(0, 12)
            .Select(i => Envelope("server.started", $"2026-08-04T10:00:{i:D2}Z", instance: "factorio"))
            .ToArray());

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        List<string> asReturned = page.Events.Select(e => e.Id).ToList();
        List<string> sorted = [.. asReturned.OrderByDescending(id => id, StringComparer.Ordinal)];
        Assert.Equal(sorted, asReturned);
    }

    // ── Paging ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FullPage_ReturnsACursor_PartialPageDoesNot()
    {
        Append("2026-08-04.ndjson", Enumerable.Range(0, 5)
            .Select(i => Envelope("server.started", $"2026-08-04T10:00:{i:D2}Z", instance: "factorio"))
            .ToArray());

        EventJournalHistory history = CreateHistory();

        EventHistoryPage full = await history.QueryAsync(new EventHistoryQuery { Limit = 5 });
        Assert.NotNull(full.NextCursorTsMs);
        Assert.NotNull(full.NextCursorId);

        EventHistoryPage partial = await history.QueryAsync(new EventHistoryQuery { Limit = 50 });
        Assert.Null(partial.NextCursorTsMs);
        Assert.Null(partial.NextCursorId);
    }

    /// <summary>
    /// Walking the cursor must visit every event exactly once. A cursor that skipped would lose
    /// history and one that repeated would show an action twice, and both look like a correct
    /// page in isolation.
    /// </summary>
    [Fact]
    public async Task QueryAsync_PagingAcrossSegments_VisitsEveryEventExactlyOnce()
    {
        foreach (int day in new[] { 4, 5, 6 })
        {
            Append($"2026-08-{day:D2}.ndjson", Enumerable.Range(0, 7)
                .Select(i => Envelope("server.started", $"2026-08-{day:D2}T10:00:{i:D2}Z", instance: "factorio"))
                .ToArray());
        }

        EventJournalHistory history = CreateHistory();
        var seen = new List<string>();
        long? cursorTs = null;
        string? cursorId = null;

        for (int guard = 0; guard < 50; guard++)
        {
            EventHistoryPage page = await history.QueryAsync(
                new EventHistoryQuery { Limit = 4, BeforeTsMs = cursorTs, BeforeId = cursorId });
            seen.AddRange(page.Events.Select(e => e.Id));

            (cursorTs, cursorId) = (page.NextCursorTsMs, page.NextCursorId);
            if (cursorTs is null)
                break;
        }

        Assert.Equal(21, seen.Count);
        Assert.Equal(21, seen.Distinct().Count());
    }

    /// <summary>
    /// Several events sharing one timestamp is the case a timestamp-only cursor cannot page
    /// through — it either re-reads the whole tied group or steps over it. The id breaks the tie.
    /// </summary>
    [Fact]
    public async Task QueryAsync_PagingThroughEventsSharingATimestamp_LosesNone()
    {
        string identical = Envelope("player.joined", "2026-08-04T10:00:00Z", instance: "factorio");
        Append("2026-08-04.ndjson", Enumerable.Repeat(identical, 9).ToArray());

        EventJournalHistory history = CreateHistory();
        var seen = new List<string>();
        long? cursorTs = null;
        string? cursorId = null;

        for (int guard = 0; guard < 20; guard++)
        {
            EventHistoryPage page = await history.QueryAsync(
                new EventHistoryQuery { Limit = 2, BeforeTsMs = cursorTs, BeforeId = cursorId });
            seen.AddRange(page.Events.Select(e => e.Id));

            (cursorTs, cursorId) = (page.NextCursorTsMs, page.NextCursorId);
            if (cursorTs is null)
                break;
        }

        Assert.Equal(9, seen.Count);
        Assert.Equal(9, seen.Distinct().Count());
    }

    [Fact]
    public async Task QueryAsync_LimitAboveTheCap_IsClamped()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory()
            .QueryAsync(new EventHistoryQuery { Limit = EventHistoryQuery.MaxLimit + 5_000 });

        Assert.Single(page.Events);
    }

    /// <summary>
    /// The cursor id may belong to another source entirely — a caller merging this history with its
    /// own rows pages both from one cursor, and the row it lands on is often not an engine event.
    /// The timestamp still bounds the page; the foreign id only ever loses a tie. Treating an
    /// unresolvable id as "no cursor" instead would restart from the newest page every time the
    /// other source supplied the boundary row, and the caller would page forever.
    /// </summary>
    [Fact]
    public async Task QueryAsync_CursorIdFromAnotherSource_StillBoundsThePage()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T09:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T12:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T15:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery
        {
            BeforeTsMs = DateTimeOffset.Parse("2026-08-04T12:00:00Z").ToUnixTimeMilliseconds(),
            BeforeId = "evt_0bded270b551c060"   // a content-derived id from the other feed
        });

        // The bound holds, and — the actual regression — the newest event is NOT back at the top of
        // the page. Returning it would mean the cursor had been ignored, and a caller walking pages
        // would loop over the same rows forever.
        Assert.NotEmpty(page.Events);
        Assert.All(page.Events, e => Assert.True(e.Ts <= DateTimeOffset.Parse("2026-08-04T12:00:00Z")));
        Assert.DoesNotContain(page.Events, e => e.Ts == DateTimeOffset.Parse("2026-08-04T15:00:00Z"));
    }

    /// <summary>
    /// A caller that pages purely by timestamp, with no tie-break, must still make progress rather
    /// than stalling on a tied group.
    /// </summary>
    [Fact]
    public async Task QueryAsync_CursorTimestampWithNoId_BoundsInclusively()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T09:00:00Z", instance: "factorio"),
            Envelope("server.started", "2026-08-04T15:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery
        {
            BeforeTsMs = DateTimeOffset.Parse("2026-08-04T09:00:00Z").ToUnixTimeMilliseconds()
        });

        Assert.Single(page.Events);
    }

    // ── Coverage and honesty ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_CoverageFrom_IsTheOldestSurvivingEvent()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T08:30:00Z", instance: "factorio"));
        Append("2026-08-05.ndjson", Envelope("server.started", "2026-08-05T09:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal(
            "2026-08-04T08:30:00Z",
            page.CoverageFrom?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    /// <summary>
    /// Asking for more than the journal can answer for is reported, not quietly served. Without
    /// this a window reaching past retention returns a partial history that reads exactly like a
    /// complete one.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WindowReachingBeforeRetention_StillReportsWhereCoverageBegins()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T08:30:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery
        {
            SinceMs = DateTimeOffset.Parse("2026-01-01T00:00:00Z").ToUnixTimeMilliseconds()
        });

        Assert.NotNull(page.CoverageFrom);
        Assert.True(page.CoverageFrom > DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    }

    [Fact]
    public async Task QueryAsync_ScanBudgetExhausted_ReportsTruncated()
    {
        Append("2026-08-04.ndjson", Enumerable.Range(0, 200)
            .Select(i => Envelope("server.started", "2026-08-04T10:00:00Z", instance: $"server{i}"))
            .ToArray());

        // A budget far below the segment, with a filter that matches nothing, forces the scan to
        // run out of budget rather than out of events.
        EventHistoryPage page = await CreateHistory(budgetBytes: 512)
            .QueryAsync(new EventHistoryQuery { Instance = "nothing-matches-this" });

        Assert.True(page.Truncated);
    }

    [Fact]
    public async Task QueryAsync_WithinBudget_DoesNotReportTruncated()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.False(page.Truncated);
    }

    // ── Degradation ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An unreadable journal and a journal with nothing to say are different facts and must not
    /// share an answer — a consumer that conflates them reports "nothing happened" when it means
    /// "I cannot see".
    /// </summary>
    [Fact]
    public async Task QueryAsync_MissingDirectory_ReportsUnreadableRatherThanEmpty()
    {
        Directory.Delete(_directory, recursive: true);

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.False(page.JournalReadable);
        Assert.Empty(page.Events);
        Assert.Null(page.CoverageFrom);
    }

    [Fact]
    public async Task QueryAsync_ReadableJournalMatchingNothing_ReportsEmptyNotUnreadable()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory()
            .QueryAsync(new EventHistoryQuery { Instance = "a-server-that-does-not-exist" });

        Assert.True(page.JournalReadable);
        Assert.Empty(page.Events);
    }

    /// <summary>
    /// The zeros an unclean shutdown leaves in a segment are a hole, not a line, and the append
    /// sitting against them landed. It is read.
    /// </summary>
    /// <remarks>
    /// Measured on this host: three producers went down mid-append and the filesystem left each
    /// segment a run of NULs running straight into the next event, with no newline between them.
    /// Read as one line that event is unparseable, so every scan reaching that far back discarded a
    /// whole event and reported its producer for output it never wrote.
    /// </remarks>
    [Fact]
    public async Task QueryAsync_AnEventWrittenAgainstACrashHole_IsRead()
    {
        AppendRaw("2026-08-04.ndjson",
            Encoding.UTF8.GetBytes(Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio") + "\n"),
            new byte[512],
            Encoding.UTF8.GetBytes(Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio") + "\n"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal(
            ["server.stopped", "server.started"],
            page.Events.Select(e => e.Type));
    }

    /// <summary>A hole with nothing behind it is not an event and is not reported as a broken one.</summary>
    [Fact]
    public async Task QueryAsync_AHoleWithNothingBehindIt_IsSkippedLikeABlankLine()
    {
        var hole = new byte[512];
        hole[^1] = (byte)'\n';

        AppendRaw("2026-08-04.ndjson",
            Encoding.UTF8.GetBytes(Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio") + "\n"),
            hole);

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("server.started", Assert.Single(page.Events).Type);
    }

    /// <summary>
    /// A hole never shifts an id. The offset an event is keyed on is where its bytes sit in the
    /// file, so healing the line a reader assembled must not renumber what follows it.
    /// </summary>
    [Fact]
    public async Task QueryAsync_AHoleDoesNotMoveTheIdsOfTheEventsBehindIt()
    {
        byte[] first = Encoding.UTF8.GetBytes(Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio") + "\n");
        byte[] second = Encoding.UTF8.GetBytes(Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio") + "\n");

        AppendRaw("2026-08-04.ndjson", first, new byte[64], second);

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        // The healed line begins where the hole began, because that is where the reader found it.
        Assert.Equal(
            AuditId.ForPosition("2026-08-04", first.Length),
            page.Events.Single(e => e.Type == "server.stopped").Id);
    }

    /// <summary>
    /// A line that cannot be read is described once, not again on every query that walks past it.
    /// </summary>
    /// <remarks>
    /// Measured on this host: a segment is never rewritten, so a line that will not parse meets every
    /// later query at the same offset, and each was reported with its full stack. Three standing
    /// faults produced 146,000 identical entries in a day — a log reporting one fact so often that
    /// nothing else in it could be found. The first report still carries everything; the repeats go
    /// to Debug, where a reader looking for them can still have them.
    /// </remarks>
    [Fact]
    public async Task QueryAsync_AStandingFault_IsReportedOnceRatherThanOnEveryQuery()
    {
        Append("2026-08-04.ndjson", "{ this is not json");

        var logger = new Mock<ILogger<EventJournalHistory>>();
        EventJournalHistory history = CreateHistory(logger: logger);

        await history.QueryAsync(new EventHistoryQuery());
        await history.QueryAsync(new EventHistoryQuery());
        await history.QueryAsync(new EventHistoryQuery());

        logger.Verify(
            l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(), (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task QueryAsync_MalformedLine_IsSkippedAndTheRestSurvives()
    {
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            "{ this is not json",
            Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal(2, page.Events.Count);
    }

    /// <summary>
    /// An event with no timestamp cannot be placed in a time-ordered history, and inventing one
    /// would put a moment that never happened into the audit trail. It is dropped instead.
    /// </summary>
    [Fact]
    public async Task QueryAsync_EventWithNoTimestamp_IsAbsentRatherThanGivenAFabricatedOne()
    {
        Append("2026-08-04.ndjson",
            """{"EventType":"server.started","Data":{"InstanceName":"factorio"}}""",
            Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio"));

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());

        Assert.Equal("server.stopped", Assert.Single(page.Events).Type);
    }

    /// <summary>
    /// Enrichment the engine did not supply stays null. A default actor would attribute an
    /// action to someone.
    /// </summary>
    [Fact]
    public async Task QueryAsync_AbsentEnrichment_StaysNull()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"));

        EventHistoryEntry entry = Assert.Single(
            (await CreateHistory().QueryAsync(new EventHistoryQuery())).Events);

        Assert.Null(entry.Actor);
        Assert.Null(entry.Origin);
        Assert.Null(entry.Hostname);
    }

    [Fact]
    public async Task QueryAsync_PayloadIsRelayedVerbatim()
    {
        Append("2026-08-04.ndjson", Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio", actor: "heisen"));

        EventHistoryEntry entry = Assert.Single(
            (await CreateHistory().QueryAsync(new EventHistoryQuery())).Events);

        Assert.Equal("heisen", entry.Actor);
        Assert.NotNull(entry.Data);
        Assert.Equal("factorio", entry.Data!.Value.GetProperty("InstanceName").GetString());
    }

    [Fact]
    public async Task QueryAsync_NullQuery_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateHistory().QueryAsync(null!));
    }

    // ── The tail path and the history read agree ─────────────────────────────────────────

    /// <summary>
    /// The invariant the split between watching and reading rests on: a consumer that sees an
    /// event arrive and a consumer that finds it in history must name it identically, with no
    /// coordination between them. Without this a surface that announces an event live and then
    /// pages back over the same history shows one fact twice, under two ids, with no way to tell
    /// they are the same.
    /// </summary>
    [Fact]
    public async Task PositionFromTheTransport_YieldsTheSameIdAsTheHistoryRead()
    {
        var positions = new List<EventPosition>();

        var reader = new EventJournalReader(
            new JournalReadOptions
            {
                EventJournalDirectory = _directory,
                EventStartPosition = EventStartPosition.Oldest
            },
            new NullEventCursorStore(),
            new Mock<ILogger<EventJournalReader>>().Object);

        reader.EventReceived += (_, position) =>
        {
            lock (positions) positions.Add(position);
            return Task.CompletedTask;
        };

        // Three events across two segments, so the agreement is proven over a segment roll and
        // not just at offset zero.
        Append("2026-08-04.ndjson",
            Envelope("server.started", "2026-08-04T10:00:00Z", instance: "factorio"),
            Envelope("server.stopped", "2026-08-04T10:05:00Z", instance: "factorio"));
        Append("2026-08-05.ndjson",
            Envelope("server.started", "2026-08-05T09:00:00Z", instance: "terraria"));

        using var cts = new CancellationTokenSource();
        Task listening = reader.StartListeningAsync(cts.Token);

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (positions)
            {
                if (positions.Count == 3) break;
            }
            await Task.Delay(25);
        }

        await cts.CancelAsync();
        try { await listening; } catch (OperationCanceledException) { /* expected */ }
        reader.Dispose();

        List<string> fromTransport;
        lock (positions)
        {
            Assert.Equal(3, positions.Count);
            fromTransport = [.. positions.Select(p => AuditId.ForPosition(p.Segment, p.Offset))];
        }

        EventHistoryPage page = await CreateHistory().QueryAsync(new EventHistoryQuery());
        List<string> fromHistory = [.. page.Events.Select(e => e.Id).Reverse()];

        Assert.Equal(fromTransport, fromHistory);
    }
}
