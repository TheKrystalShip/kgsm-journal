using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.KGSM.Core.Interfaces;
using TheKrystalShip.KGSM.Core.Models;
using TheKrystalShip.KGSM.Events;
using TheKrystalShip.KGSM.Lifecycle;
using TheKrystalShip.KGSM.Services;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// Binds the emitter, the catalog and the payload classes to one set of field names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The drift this prevents is live elsewhere in the ecosystem.</b> A producer writes literal
/// property names into a payload from its own repository, and the reader declares a class with
/// matching properties in another — two spellings of one set of names, bound by nothing but
/// case-insensitive matching. A rename on either side yields a field that silently reads back as its
/// default, and nothing fails.
/// </para>
/// <para>
/// These four events are emitted from seven repositories into one payload class each, so the names are
/// held once in <see cref="LeafLifecycleFields"/> and everything else is checked against what an
/// emitter actually wrote. Not against a list in this file: a list here would be a fourth spelling.
/// </para>
/// </remarks>
public sealed class LeafLifecycleContractTests : IDisposable
{
    private const string Producer = "kgsm-monitor";

    /// <summary>
    /// Envelope-level properties <see cref="KgsmEventDataBase"/> carries. They are populated by the
    /// reader from the envelope rather than from the payload, so a payload never writes them and they
    /// are not part of what these events declare.
    /// </summary>
    private static readonly HashSet<string> EnvelopeProperties =
        new(["Timestamp", "Actor", "Origin"], StringComparer.Ordinal);

    private readonly string _root;
    private readonly string _directory;
    private readonly DateTimeOffset _now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    public LeafLifecycleContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kgsm-lifecycle-contract", Path.GetRandomFileName());
        _directory = JournalLayout.DirectoryFor(Producer, _root);
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    [Theory]
    [MemberData(nameof(EveryLifecycleEvent))]
    public void What_the_emitter_writes_is_what_the_payload_class_declares(string eventType)
    {
        // The core binding. Both sides are read from the artefacts themselves — the JSON an emitter
        // produced, and the properties a class declares — so neither can be satisfied by editing this
        // test.
        IReadOnlyCollection<string> written = FieldsWrittenFor(eventType);
        Type payload = KgsmEventCatalog.Describe(eventType).PayloadType!;

        Assert.Equal(JsonNamesOf(payload).Order(), written.Order());
    }

    [Theory]
    [MemberData(nameof(EveryLifecycleEvent))]
    public void What_the_emitter_writes_is_what_the_catalog_classifies(string eventType)
    {
        // A field a surface has no classification for is one it has to guess how to render. The
        // catalog and the emitter agreeing is what stops a field from arriving unclassified.
        IReadOnlyCollection<string> written = FieldsWrittenFor(eventType);
        EventDescriptor descriptor = KgsmEventCatalog.Describe(eventType);

        Assert.Equal(descriptor.Fields.Select(static f => f.Name).Order(), written.Order());
    }

    [Theory]
    [MemberData(nameof(EveryLifecycleEvent))]
    public void An_emitted_line_reads_back_into_its_payload_class(string eventType)
    {
        // The registration this proves is the one that fails at RUNTIME, not at build: the library is
        // reflection-free for its AOT consumers, so a payload type missing from JournalJsonContext throws
        // NotSupportedException the first time a consumer reads one of these lines.
        string data = DataJsonFor(eventType);
        Type payload = KgsmEventCatalog.Describe(eventType).PayloadType!;

        JsonTypeInfo? info = JournalJsonContext.Default.GetTypeInfo(payload);

        Assert.True(info is not null, $"{payload.Name} is not registered in JournalJsonContext");

        object? read = JsonSerializer.Deserialize(data, info!);

        Assert.NotNull(read);

        // Every property populated, not merely a successful parse: a name that does not match reads
        // back as a default, which is exactly the silent failure and it deserializes perfectly.
        //
        // Compared against a FRESH INSTANCE rather than against default(T). A payload's string
        // properties are initialised to string.Empty, which is not null — so an unmatched name leaves
        // "" behind and a default(T) comparison waves it through. That hole was real here and this is
        // what closed it.
        object untouched = Activator.CreateInstance(payload)!;

        foreach (PropertyInfo property in Declared(payload))
        {
            object? value = property.GetValue(read);

            Assert.True(
                value is not null && !Equals(value, property.GetValue(untouched)),
                $"{payload.Name}.{property.Name} read back unset — the emitter and the class disagree "
                + "about its name");
        }
    }

    [Fact]
    public void Every_event_the_emitter_can_write_is_one_the_catalog_knows()
    {
        // The catalog answers Unrecognized for a type it has never heard of, which a surface renders as
        // best it can. An emitter shipping an event the reader has no descriptor for is a line nothing
        // classifies.
        foreach (string type in LifecycleEventTypes())
            Assert.True(KgsmEventCatalog.Describe(type).Known, $"{type} is not in the catalog");
    }

    [Fact]
    public void The_catalog_reads_a_lifecycle_event_as_being_about_a_leaf()
    {
        foreach (string type in LifecycleEventTypes())
        {
            EventDescriptor descriptor = KgsmEventCatalog.Describe(type);

            Assert.Equal(EventSubject.Service, descriptor.Subject);
            Assert.Equal(EventWeight.Fact, descriptor.Weight);
        }
    }

    [Fact]
    public void A_lifecycle_payload_never_names_a_leaf()
    {
        // The producer is established from the journal a line was read out of, and a reader can check
        // that. A leaf id inside the payload would be a claim it cannot check, free to disagree — which
        // is the whole reason these do not derive from ServiceEventData.
        foreach (string type in LifecycleEventTypes())
        {
            Type payload = KgsmEventCatalog.Describe(type).PayloadType!;

            Assert.False(
                typeof(ServiceEventData).IsAssignableFrom(payload),
                $"{payload.Name} derives from ServiceEventData, which requires a Leaf id");

            Assert.DoesNotContain("Leaf", JsonNamesOf(payload), StringComparer.Ordinal);
            Assert.DoesNotContain("Version", JsonNamesOf(payload), StringComparer.Ordinal);
        }
    }

    [Fact]
    public void Every_field_name_the_contract_holds_is_one_some_event_uses()
    {
        // The other direction: a constant nothing writes is a name that has quietly gone out of use,
        // and it would keep looking like part of the contract.
        HashSet<string> used = [];

        foreach (string type in LifecycleEventTypes())
            used.UnionWith(FieldsWrittenFor(type));

        foreach (string declared in ConstantsOf(typeof(LeafLifecycleFields)))
            Assert.Contains(declared, used);
    }

    public static TheoryData<string> EveryLifecycleEvent()
    {
        var data = new TheoryData<string>();

        foreach (string type in LifecycleEventTypes())
            data.Add(type);

        return data;
    }

    /// <summary>
    /// The event types from the contract class itself, so an event added there is covered here without
    /// anybody remembering to add it.
    /// </summary>
    private static IReadOnlyList<string> LifecycleEventTypes() =>
        [.. ConstantsOf(typeof(LeafLifecycleEvents))];

    private static IReadOnlyList<string> ConstantsOf(Type type) =>
        [.. type.GetFields()
            .Where(static f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(static f => (string)f.GetRawConstantValue()!)];

    /// <summary>The JSON names a payload class declares, envelope properties excluded.</summary>
    private static IReadOnlyList<string> JsonNamesOf(Type payload) =>
        [.. Declared(payload).Select(static p =>
            p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)];

    private static IEnumerable<PropertyInfo> Declared(Type payload) =>
        payload.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => !EnvelopeProperties.Contains(p.Name));

    /// <summary>The property names present in the payload an emitter actually wrote.</summary>
    private IReadOnlyCollection<string> FieldsWrittenFor(string eventType)
    {
        using JsonDocument document = JsonDocument.Parse(DataJsonFor(eventType));

        return [.. document.RootElement.EnumerateObject().Select(static p => p.Name)];
    }

    /// <summary>
    /// Emits one of each event with every field populated, and returns that event's payload.
    /// </summary>
    /// <remarks>
    /// Through the real emitter into a real journal. A hand-written JSON string here would be a second
    /// spelling of the thing under test, which is the defect rather than the check.
    /// </remarks>
    private string DataJsonFor(string eventType)
    {
        var options = new EventJournalWriterOptions
        {
            Producer = Producer,
            Directory = _directory,
            Hostname = "hotrod",
            ProducerVersion = "2.7.1+test",
            Clock = () => _now,
        };

        IEventJournalWriter writer = new EventJournalWriter(
            options, NullLogger<EventJournalWriter>.Instance);

        DateTimeOffset start = _now.AddMinutes(-5);

        var lifecycle = new LeafLifecycle(
            writer, NullLogger<LeafLifecycle>.Instance, () => _now, () => start);

        // Each event with every optional field supplied, so a field that is merely absent cannot pass
        // for one that matches.
        if (eventType == LeafLifecycleEvents.Ready)
            lifecycle.MarkReady("the first frame landed");
        else if (eventType == LeafLifecycleEvents.Degraded)
            lifecycle.MarkDegraded("net-meter", "bpf map pin absent");
        else if (eventType == LeafLifecycleEvents.Recovered)
        {
            lifecycle.MarkDegraded("net-meter", "bpf map pin absent");
            lifecycle.MarkRecovered("net-meter");
        }
        else if (eventType == LeafLifecycleEvents.Stopping)
            lifecycle.MarkStopping(LeafStopReason.Signal);
        else
            Assert.Fail($"{eventType} has no emitter call in this test — a new event needs one");

        string[] lines = File.ReadAllLines(Path.Combine(_directory, $"{_now:yyyy-MM-dd}.ndjson"));
        string line = Assert.Single(lines, l => TypeOf(l) == eventType);

        return JsonDocument.Parse(line).RootElement.GetProperty("Data").GetRawText();
    }

    private static string TypeOf(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("EventType").GetString()!;
}
