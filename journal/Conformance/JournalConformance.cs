using System.Globalization;
using System.Text.Json;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Conformance;

/// <summary>
/// Reads what producers actually wrote and reports where it does not match the envelope contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Producer-agnostic by construction.</b> Nothing here knows which components exist: it is handed
/// journals and checks whatever it is handed. A producer added to this ecosystem later is covered the
/// moment its journal exists, without anybody remembering to cover it — which is the property that
/// matters, because the drift this catches is precisely the kind nobody thinks to look for.
/// </para>
/// <para>
/// <b>Mechanism, never policy.</b> No rule looks at an event type or a payload field. What a producer
/// records, and when, is its own business; that it spells the envelope the way every reader parses it
/// is not.
/// </para>
/// <para>
/// <b>An old line is allowed to look old.</b> A line records what the build that wrote it produced,
/// so a journal's history legitimately contains shapes a current build would no longer write. A caller
/// checking a host for conformance therefore samples the newest lines — the ones the deployed builds
/// wrote — and that is why the sample size is the caller's to choose rather than "everything".
/// </para>
/// </remarks>
public static class JournalConformance
{
    /// <summary>Envelope fields every line carries.</summary>
    public static readonly IReadOnlyList<string> RequiredFields = ["V", "EventType", "Data", "Timestamp"];

    /// <summary>
    /// Envelope fields a line may carry, including <c>Id</c> and the three reserved for correlation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reserved ones are listed so a producer that starts populating them is not reported as
    /// having invented a field — they are part of the contract whether or not anything writes them.
    /// <b>A field must be listed here before any producer emits it</b>, or every line that producer
    /// writes is reported as carrying an unknown field.
    /// </para>
    /// <para>
    /// <c>Id</c> is optional and stays optional. Lines written before it existed are on disk for as
    /// long as retention holds them, so a reader meets one that has no id for years yet — and absent
    /// means <b>unknown</b>, never a mismatch.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> OptionalFields =
        [
            "Actor", "Origin", "Hostname", "ProducerVersion", "Id", "OpId", "RunId", "During",
            "Severity", "Outcome", "Summary",
        ];

    /// <summary>
    /// The envelope versions this reader understands.
    /// </summary>
    /// <remarks>
    /// More than one, because a journal holds what earlier builds wrote for as long as retention keeps
    /// it, and a line that predates a field is not malformed — it is old. A reader that understood only
    /// the newest version would report a host's whole history as broken the day a field was added.
    /// </remarks>
    public static readonly IReadOnlyList<int> SupportedSchemaVersions = [1, 2];

    /// <summary>How many lines of a journal a host check reads when the caller names no number.</summary>
    /// <remarks>
    /// One is enough to catch a producer's spelling, because a producer spells every line the same
    /// way. More is for a caller that wants the shape of a whole day.
    /// </remarks>
    public const int DefaultLinesPerJournal = 1;

    private static readonly HashSet<string> KnownFields =
        new(RequiredFields.Concat(OptionalFields), StringComparer.Ordinal);

    /// <summary>
    /// Checks one journal line.
    /// </summary>
    /// <param name="producer">The producer whose journal the line came from.</param>
    /// <param name="segment">The segment file name, for locating a finding.</param>
    /// <param name="lineNumber">The 1-based line number, for locating a finding.</param>
    /// <param name="line">The raw line.</param>
    /// <returns>Every rule the line breaks; empty when it conforms.</returns>
    public static IReadOnlyList<ConformanceFinding> CheckLine(
        string producer, string? segment, int lineNumber, string? line)
    {
        var findings = new List<ConformanceFinding>();
        void Add(string rule, string detail) =>
            findings.Add(new ConformanceFinding(producer, segment, lineNumber, rule, detail));

        // A hole is the filesystem's, not the producer's: reporting the zeros against the envelope
        // contract blames a component for bytes it never wrote. What landed either side of the hole
        // is checked normally, and a line that is nothing else is named for what it is.
        bool holed = JournalLine.IsHoled(line);
        line = JournalLine.WithoutHole(line);

        if (string.IsNullOrWhiteSpace(line))
        {
            Add(ConformanceRule.LineParses, holed
                ? "an unclean shutdown left a hole here; the bytes it stands in for were never written to disk"
                : "the line is blank");
            return findings;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            // A malformed line is not cosmetic: a consumer's cursor is a byte offset, so one bad line
            // desynchronizes every reader past it. Nothing else about the line can be checked.
            Add(ConformanceRule.LineParses, $"not valid JSON ({ex.Message})");
            return findings;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                Add(ConformanceRule.LineParses, $"expected a JSON object, found {root.ValueKind}");
                return findings;
            }

            CheckSchemaVersion(root, Add);
            CheckEventType(root, Add);
            CheckData(root, Add);
            CheckTimestamp(root, Add);
            CheckAbsentSpelling(root, Add);
            CheckActor(root, Add);
            CheckProducerVersion(root, Add);
            CheckEventId(root, Add);
            CheckSeverity(root, Add);
            CheckOutcome(root, Add);
            CheckSummary(root, Add);
            CheckUnknownFields(root, Add);
        }

        return findings;
    }

    /// <summary>
    /// Checks one producer's journal: its layout, its segment names, and its most recent lines.
    /// </summary>
    /// <param name="producer">The producer id, as the caller established it.</param>
    /// <param name="directory">The directory that producer appends to.</param>
    /// <param name="lines">
    /// How many of the newest lines to read. Zero or less reads none, which checks layout only.
    /// </param>
    /// <returns>What was found, and what the journal said about the host it was written on.</returns>
    /// <exception cref="ArgumentException">Thrown when the producer id or directory is blank.</exception>
    public static JournalReport CheckJournal(
        string producer, string directory, int lines = DefaultLinesPerJournal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer, nameof(producer));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory, nameof(directory));

        var findings = new List<ConformanceFinding>();

        CheckDirectoryAttribution(producer, directory, findings);

        string[] segments;

        try
        {
            if (!Directory.Exists(directory))
            {
                // A producer with no directory has recorded nothing, which is an honest state and not
                // this check's business. A caller that expected it to exist says so itself.
                return new JournalReport(producer, directory, findings, null, 0, null);
            }

            segments = Directory.GetFiles(directory, "*.ndjson");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            findings.Add(new ConformanceFinding(
                producer, null, 0, ConformanceRule.JournalReadable,
                $"the journal at {directory} could not be listed ({ex.Message})"));

            return new JournalReport(producer, directory, findings, null, 0, null);
        }

        Array.Sort(segments, StringComparer.Ordinal);
        CheckSegmentNames(producer, segments, findings);

        if (segments.Length == 0 || lines <= 0)
            return new JournalReport(producer, directory, findings, null, 0, null);

        string newest = segments[^1];
        string name = Path.GetFileName(newest);

        (int Number, string Text)[] tail;

        try
        {
            tail = Tail(newest, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            findings.Add(new ConformanceFinding(
                producer, name, 0, ConformanceRule.JournalReadable,
                $"the segment could not be read ({ex.Message})"));

            return new JournalReport(producer, directory, findings, null, 0, name);
        }

        string? hostname = null;

        foreach ((int number, string text) in tail)
        {
            findings.AddRange(CheckLine(producer, name, number, text));
            hostname ??= HostnameOf(text);
        }

        return new JournalReport(producer, directory, findings, hostname, tail.Length, name);
    }

    /// <summary>
    /// Checks every journal on a host, including the things only comparing them can reveal.
    /// </summary>
    /// <remarks>
    /// The cross-producer half is the reason this is not just <see cref="CheckJournal"/> in a loop:
    /// a hostname that two producers spell differently is conformant in each journal read alone and
    /// wrong the moment they are merged, which is how every consumer reads them.
    /// </remarks>
    /// <param name="journals">The journals to check, as producer and directory.</param>
    /// <param name="lines">How many of the newest lines to read from each.</param>
    /// <returns>Every journal's report, plus the findings that only exist across journals.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journals"/> is null.</exception>
    public static HostReport CheckHost(
        IEnumerable<JournalTarget> journals, int lines = DefaultLinesPerJournal)
    {
        ArgumentNullException.ThrowIfNull(journals, nameof(journals));

        List<JournalReport> reports = [.. journals.Select(j => CheckJournal(j.Producer, j.Directory, lines))];

        var crossJournal = new List<ConformanceFinding>();

        // The hostname every producer that named one agreed on. Producers that named none are not
        // disagreeing — the field is optional, and absence is not a competing answer.
        List<JournalReport> named = [.. reports.Where(static r => r.Hostname is not null)];
        string[] distinct = [.. named.Select(static r => r.Hostname!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        if (distinct.Length > 1)
        {
            string spread = string.Join(", ", named.Select(static r => $"{r.Producer}={r.Hostname}"));

            foreach (JournalReport report in named)
            {
                crossJournal.Add(new ConformanceFinding(
                    report.Producer, report.Segment, 0, ConformanceRule.HostnameAgrees,
                    $"producers on one host name it {distinct.Length} different ways ({spread})"));
            }
        }

        return new HostReport(reports, crossJournal);
    }

    private static void CheckSchemaVersion(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("V", out JsonElement version))
        {
            add(ConformanceRule.SchemaVersion, "no V — a reader cannot tell which contract this is");
            return;
        }

        if (version.ValueKind != JsonValueKind.Number)
        {
            add(ConformanceRule.SchemaVersion, $"V is {version.ValueKind}, expected a number");
            return;
        }

        if (!version.TryGetInt32(out int value) || !SupportedSchemaVersions.Contains(value))
        {
            add(ConformanceRule.SchemaVersion,
                $"V is {version} — this reader understands "
                + string.Join(" and ", SupportedSchemaVersions));
        }
    }

    private static void CheckEventType(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("EventType", out JsonElement type))
        {
            add(ConformanceRule.EventType, "no EventType");
            return;
        }

        if (type.ValueKind != JsonValueKind.String)
        {
            add(ConformanceRule.EventType, $"EventType is {type.ValueKind}, expected a string");
            return;
        }

        string value = type.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            add(ConformanceRule.EventType, "EventType is blank");
            return;
        }

        // Dash on a command line, underscore on the wire. A type that reaches a journal still dashed
        // is a type no consumer matches, and it fails silently — a subscriber simply never fires.
        if (value.Contains('-', StringComparison.Ordinal))
            add(ConformanceRule.EventType, $"'{value}' is dash-separated; the wire spells types with underscores");

        if (value.Any(char.IsUpper))
            add(ConformanceRule.EventType, $"'{value}' is not lowercase");

        if (value.Any(char.IsWhiteSpace))
            add(ConformanceRule.EventType, $"'{value}' contains whitespace");
    }

    /// <summary>
    /// Checks the severity's spelling, never the judgement behind it.
    /// </summary>
    private static void CheckSeverity(JsonElement root, Action<string, string> add) =>
        CheckEnumField(root, "Severity", ConformanceRule.Severity, EventSeverities.All, add);

    /// <summary>Checks the outcome's spelling.</summary>
    private static void CheckOutcome(JsonElement root, Action<string, string> add) =>
        CheckEnumField(root, "Outcome", ConformanceRule.Outcome, EventOutcomes.All, add);

    /// <summary>
    /// Checks that a summary is a string, and stops there.
    /// </summary>
    /// <remarks>
    /// Its wording is content and belongs to whoever raised the event. The only thing a reader needs
    /// from this field is that it is text it can put on a screen.
    /// </remarks>
    private static void CheckSummary(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("Summary", out JsonElement summary)) return;
        if (summary.ValueKind == JsonValueKind.Null) return;

        if (summary.ValueKind != JsonValueKind.String)
            add(ConformanceRule.Summary, $"Summary is {summary.ValueKind}, expected a string");
    }

    /// <summary>One closed-vocabulary envelope field, checked against the spellings it defines.</summary>
    private static void CheckEnumField(
        JsonElement root, string field, string rule, IReadOnlyList<string> allowed,
        Action<string, string> add)
    {
        if (!root.TryGetProperty(field, out JsonElement value)) return;
        if (value.ValueKind == JsonValueKind.Null) return;

        if (value.ValueKind != JsonValueKind.String)
        {
            add(rule, $"{field} is {value.ValueKind}, expected a string");
            return;
        }

        string spelling = value.GetString() ?? string.Empty;

        if (!allowed.Contains(spelling, StringComparer.Ordinal))
        {
            add(rule,
                $"{field} is '{spelling}' — the defined spellings are "
                + string.Join(", ", allowed));
        }
    }

    private static void CheckData(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("Data", out JsonElement data))
        {
            add(ConformanceRule.Data, "no Data — every reader keys an event's subject off a property of it");
            return;
        }

        if (data.ValueKind != JsonValueKind.Object)
            add(ConformanceRule.Data, $"Data is {data.ValueKind}, expected an object");
    }

    private static void CheckTimestamp(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("Timestamp", out JsonElement stamp))
        {
            add(ConformanceRule.Timestamp, "no Timestamp");
            return;
        }

        if (stamp.ValueKind != JsonValueKind.String)
        {
            add(ConformanceRule.Timestamp, $"Timestamp is {stamp.ValueKind}, expected a string");
            return;
        }

        string value = stamp.GetString() ?? string.Empty;

        // Exact, not "parseable". Several journals merged on second granularity order arbitrarily
        // inside each second, which is exactly where causally adjacent events sit — so a producer
        // writing a coarser or differently-shaped stamp is not a cosmetic difference.
        if (!DateTimeOffset.TryParseExact(
                value, EventJournalWriter.TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
        {
            add(ConformanceRule.Timestamp,
                $"'{value}' is not millisecond-precision UTC ({EventJournalWriter.TimestampFormat})");
        }
    }

    private static void CheckAbsentSpelling(JsonElement root, Action<string, string> add)
    {
        foreach (string field in OptionalFields)
        {
            if (!root.TryGetProperty(field, out JsonElement value))
                continue;

            if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            {
                add(ConformanceRule.AbsentSpelling,
                    $"{field} is an empty string — absence is spelled by omitting the field or writing null");
            }
        }
    }

    private static void CheckActor(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("Actor", out JsonElement actor) || actor.ValueKind != JsonValueKind.String)
            return;

        string value = actor.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return;

        // An actor is `provider:name`, the form every reader splits it back into. A name with no
        // provider in front of it is the shape an OS username takes, and an OS username is who owns
        // the process rather than who asked for the action — so it names the wrong principal on an
        // audit record even when the string itself is a real person's login.
        int separator = value.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            add(ConformanceRule.Actor, $"'{value}' names somebody but no provider — an actor is 'provider:name'");
            return;
        }

        if (separator == 0 || separator == value.Length - 1)
            add(ConformanceRule.Actor, $"'{value}' is qualified but one side of the ':' is empty");
    }

    private static void CheckProducerVersion(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("ProducerVersion", out JsonElement version)
            || version.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string value = version.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return;

        if (IsFourPartAssemblyVersion(value))
        {
            add(ConformanceRule.ProducerVersionShape,
                $"'{value}' is a four-part assembly version — no released package is numbered that way, "
                + "so it matches no tag, no pin and no other producer's stamp");
        }
    }

    /// <summary>
    /// Whether a version is the four-part number an assembly carries rather than the one it ships as.
    /// </summary>
    /// <remarks>
    /// The single measurable shape of the drift: an assembly exposes both, and a producer reaching for
    /// the wrong one stamps <c>2.3.0.0</c> where every other producer stamps <c>2.3.0+&lt;sha&gt;</c>.
    /// Nothing else is rejected — a version is otherwise whatever a repo numbers its release.
    /// </remarks>
    /// <param name="version">The version as written.</param>
    /// <returns>True when it is four all-numeric dot-separated parts.</returns>
    public static bool IsFourPartAssemblyVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        string[] parts = version.Split('.');

        if (parts.Length != 4)
            return false;

        foreach (string part in parts)
        {
            if (part.Length == 0 || !part.All(char.IsAsciiDigit))
                return false;
        }

        return true;
    }

    private static void CheckEventId(JsonElement root, Action<string, string> add)
    {
        if (!root.TryGetProperty("Id", out JsonElement id) || id.ValueKind == JsonValueKind.Null)
        {
            // Absent is a spelling, and it is the whole back catalogue plus any producer whose shell
            // is too old to mint one. It is never a finding.
            return;
        }

        if (id.ValueKind != JsonValueKind.String)
        {
            add(ConformanceRule.EventIdShape, $"Id is {id.ValueKind}, expected a string");
            return;
        }

        string value = id.GetString() ?? string.Empty;

        // The empty string is CheckAbsentSpelling's finding. Reporting it twice for one defect makes
        // a report harder to read without telling anybody anything more.
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!IsWellFormedEventId(value))
        {
            add(ConformanceRule.EventIdShape,
                $"'{value}' is not a lowercase hyphenated UUIDv7");
        }
    }

    /// <summary>
    /// Whether an id is spelled the way the contract requires: a lowercase, hyphenated UUID whose
    /// version nibble is 7 and whose variant is the RFC 4122 one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately stricter than <see cref="Guid.TryParse(string, out Guid)"/>, in both directions
    /// that matter. <b>Case</b>, because an id is compared as text by everything that stores one — the
    /// reactor's ledger, the audit cursor — and an uppercase spelling of the same id is a different
    /// string, so a producer writing one would look like a producer writing different events.
    /// <b>Version</b>, because a v4 parses as a uuid perfectly well while losing the time-ordering the
    /// format was chosen for, and nothing downstream is in a position to notice the loss.
    /// </para>
    /// <para>
    /// This is the shape check, not an identity check: it says the id could name an event, never that
    /// it names the right one. Comparing an id against the line it was stored for is a consumer's job,
    /// because only a consumer holds the earlier reading to compare against.
    /// </para>
    /// </remarks>
    /// <param name="id">The id as written, or null.</param>
    /// <returns>True when the id is well formed; false for null, empty, or any other shape.</returns>
    public static bool IsWellFormedEventId(string? id)
    {
        if (id is not { Length: 36 })
            return false;

        for (int i = 0; i < 36; i++)
        {
            char c = id[i];

            if (i is 8 or 13 or 18 or 23)
            {
                if (c != '-')
                    return false;
            }
            else if (!char.IsAsciiDigit(c) && c is < 'a' or > 'f')
            {
                return false;
            }
        }

        // The version nibble leads the third group; the variant nibble leads the fourth.
        return id[14] == '7' && id[19] is '8' or '9' or 'a' or 'b';
    }

    private static void CheckUnknownFields(JsonElement root, Action<string, string> add)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name))
            {
                add(ConformanceRule.UnknownField,
                    $"'{property.Name}' is not an envelope field; event-specific values belong in Data");
            }
        }
    }

    private static void CheckDirectoryAttribution(
        string producer, string directory, List<ConformanceFinding> findings)
    {
        string? attributed = JournalLayout.ProducerOf(directory);

        // Null is not a fault. A journal at a configured path outside the state root is invisible to a
        // scan and is instead named explicitly by the consumer that knows of it, which is how the
        // engine's configurable directory works. A non-null answer that disagrees is the fault: it
        // means a scan of this host would file these lines under somebody else's name.
        if (attributed is not null && !string.Equals(attributed, producer, StringComparison.Ordinal))
        {
            findings.Add(new ConformanceFinding(
                producer, null, 0, ConformanceRule.ProducerMatchesDirectory,
                $"lines here would be attributed to '{attributed}', because that is what {directory} says"));
        }
    }

    private static void CheckSegmentNames(
        string producer, string[] segments, List<ConformanceFinding> findings)
    {
        foreach (string segment in segments)
        {
            string stem = Path.GetFileNameWithoutExtension(segment);

            // A segment's age comes from its name, so a name that is not a date is a segment retention
            // cannot reason about — it is kept forever rather than deleted, which is the safe half of
            // the problem, but it also sorts unpredictably against the ones that are dates.
            if (!DateOnly.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                findings.Add(new ConformanceFinding(
                    producer, Path.GetFileName(segment), 0, ConformanceRule.SegmentName,
                    "a segment is named for the day it holds (yyyy-MM-dd.ndjson); retention ages it by that name"));
            }
        }
    }

    private static string? HostnameOf(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return document.RootElement.TryGetProperty("Hostname", out JsonElement host)
                && host.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(host.GetString())
                    ? host.GetString()
                    : null;
        }
        catch (JsonException)
        {
            // Already reported by the line check; a malformed line simply says nothing about the host.
            return null;
        }
    }

    /// <summary>
    /// The last <paramref name="count"/> non-empty lines of a file, each with its 1-based position.
    /// </summary>
    /// <remarks>
    /// The number is the line's position in the file, not in the sample: a finding is only actionable
    /// if the number in it is the one an editor jumps to. Streamed rather than read whole, because a
    /// busy producer's segment is the one most worth checking and the least worth loading.
    /// </remarks>
    private static (int Number, string Text)[] Tail(string path, int count)
    {
        var window = new Queue<(int, string)>(count);
        int number = 0;

        foreach (string line in File.ReadLines(path))
        {
            number++;

            if (line.Length == 0)
                continue;

            if (window.Count == count)
                window.Dequeue();

            window.Enqueue((number, line));
        }

        return [.. window];
    }
}
