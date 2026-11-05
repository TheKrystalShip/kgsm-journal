using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Events;

/// <summary>
/// Binds the reactor's payload classes to the field names its emitter writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The catalog's own drift tests cannot see this.</b> They compare a descriptor's fields against
/// the payload's <em>C# property names</em>, which is the right question for classification and the
/// wrong one for binding: a property whose <see cref="JsonPropertyNameAttribute"/> drifts from the
/// constant keeps its C# name, stays classified, and silently reads back as its default at runtime.
/// </para>
/// <para>
/// <b>And the producer is a different repository.</b> The reactor writes these from its own copy of
/// the field names, so nothing in this solution fails when the two spellings part company. The
/// literals below are lines the leaf actually wrote, which is the only check that reaches across the
/// gap — everything else here would be this library agreeing with itself.
/// </para>
/// </remarks>
public sealed class ReactorEventContractTests
{
    /// <summary>
    /// Envelope-level properties the reader populates from the envelope rather than the payload.
    /// </summary>
    private static readonly HashSet<string> EnvelopeProperties =
        new(["Timestamp", "Actor", "Origin"], StringComparer.Ordinal);

    /// <summary>Every event the reactor produces, with the class its payload reads back into.</summary>
    public static TheoryData<string, Type> Events => new()
    {
        { ReactorEvents.Decided, typeof(ReactorDecidedEventData) },
        { ReactorEvents.Proposed, typeof(ReactorProposedEventData) },
        { ReactorEvents.Resolved, typeof(ReactorResolvedEventData) },
        { ReactorEvents.Acted, typeof(ReactorActedEventData) },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void EveryPayloadPropertyBindsToTheNameTheCatalogClassifies(string type, Type payload)
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe(type);
        Assert.True(descriptor.Known, $"{type} is not classified");

        var document = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (EventField field in descriptor.Fields)
            document[field.Name] = ValueFor(payload, field.Name);

        object? read = JsonSerializer.Deserialize(JsonSerializer.Serialize(document), payload);
        Assert.NotNull(read);

        object fresh = Activator.CreateInstance(payload)!;

        List<string> unbound = [];
        foreach (PropertyInfo property in Declared(payload))
        {
            if (Equals(property.GetValue(read), property.GetValue(fresh)))
                unbound.Add(property.Name);
        }

        Assert.True(unbound.Count == 0,
            $"{type}: these properties did not bind from the names the catalog classifies, so the "
            + "payload's [JsonPropertyName] has drifted from ReactorEventFields: "
            + string.Join(", ", unbound.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// A decision the leaf actually wrote reads back whole.
    /// </summary>
    /// <remarks>
    /// A real line, captured from a host's journal. The producer is a different repository writing
    /// from its own copy of the field names, so this is the only assertion here that can catch the two
    /// parting company — and the failure it catches is silent, because a property that fails to bind
    /// keeps its initialised value and reads as an ordinary empty answer.
    /// </remarks>
    [Fact]
    public void ADecisionTheLeafWroteReadsBackWhole()
    {
        const string payload = """
            {
              "Rule": "authored_probe",
              "RuleAuthor": "local:claude",
              "Subject": "projectzomboid",
              "SubjectKind": "instance",
              "Severity": "info",
              "Mode": "observe",
              "Outcome": "settled",
              "Reason": "projectzomboid holds 1187MB",
              "Action": "none",
              "ActionInstance": null,
              "DecisionId": "882485df8774c4b9337bdc789e9e09dd",
              "OpenedAt": "2026-08-28T06:26:20.0571388+00:00",
              "SourceProducer": "authored_probe",
              "SourceSegment": "projectzomboid",
              "SourceOffset": 0,
              "SourceEventId": null
            }
            """;

        ReactorDecidedEventData? read =
            JsonSerializer.Deserialize<ReactorDecidedEventData>(payload);

        Assert.NotNull(read);
        Assert.Equal("authored_probe", read.Rule);
        Assert.Equal("local:claude", read.RuleAuthor);
        Assert.Equal("projectzomboid", read.Subject);
        Assert.Equal("instance", read.SubjectKind);
        Assert.Equal("info", read.Severity);
        Assert.Equal("observe", read.Mode);
        Assert.Equal(ReactorOutcomes.Settled, read.Outcome);
        Assert.Equal("projectzomboid holds 1187MB", read.Reason);
        Assert.Equal("none", read.Action);
        Assert.Null(read.ActionInstance);
        Assert.Equal("882485df8774c4b9337bdc789e9e09dd", read.DecisionId);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 28, 6, 26, 20, TimeSpan.Zero).AddTicks(571388),
            read.OpenedAt);
        Assert.Equal("authored_probe", read.SourceProducer);
        Assert.Equal(0, read.SourceOffset);
        Assert.Null(read.SourceEventId);
    }

    /// <summary>
    /// A line written before a field existed still reads, and the field it lacks reads as unknown.
    /// </summary>
    /// <remarks>
    /// <b>Retention outlives a release.</b> A journal holds what earlier builds wrote for as long as
    /// its segments are kept, so a consumer reads shapes the current producer no longer emits. Null is
    /// what the absence means — nobody is known to have shaped that rule — and a reader that treated an
    /// older line as a parse failure would lose a fortnight of decisions on the day a field was added.
    /// </remarks>
    [Fact]
    public void ADecisionOlderThanAFieldStillReads()
    {
        const string payload = """
            {
              "Rule": "memory_declaration_drift",
              "Subject": "projectzomboid",
              "SubjectKind": "instance",
              "Severity": "info",
              "Mode": "observe",
              "Outcome": "unreadable",
              "Reason": "observations of projectzomboid span 0.0 days, short of the 2 a world's growth shows up over",
              "Action": "none",
              "ActionInstance": null,
              "DecisionId": "e2cec3f6211a672f896a6a92bc6fa79b",
              "OpenedAt": "2026-08-27T19:04:11.8908654+00:00",
              "SourceProducer": "memory_declaration_drift",
              "SourceSegment": "projectzomboid",
              "SourceOffset": 0,
              "SourceEventId": null
            }
            """;

        ReactorDecidedEventData? read =
            JsonSerializer.Deserialize<ReactorDecidedEventData>(payload);

        Assert.NotNull(read);
        Assert.Equal("memory_declaration_drift", read.Rule);
        Assert.Equal(ReactorOutcomes.Unreadable, read.Outcome);
        Assert.Null(read.RuleAuthor);
    }

    /// <summary>
    /// The outcome spellings are the ones the leaf writes.
    /// </summary>
    /// <remarks>
    /// Lower case, which is not what the leaf's own enum names look like — it lowercases them on the
    /// way out, deliberately, so that every enumerated value in every payload on this host is spelled
    /// the same way. A consumer comparing against a C# enum name would match nothing.
    /// </remarks>
    [Fact]
    public void TheOutcomesAreSpelledTheWayTheLeafWritesThem()
    {
        string[] spellings =
        [
            ReactorOutcomes.Fired, ReactorOutcomes.Settled, ReactorOutcomes.Suppressed,
            ReactorOutcomes.Ceilinged, ReactorOutcomes.Superseded, ReactorOutcomes.Unreadable,
        ];

        Assert.All(spellings, s => Assert.Equal(s.ToLowerInvariant(), s));
        Assert.Equal(spellings.Length, spellings.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// No payload names the producer.
    /// </summary>
    /// <remarks>
    /// The journal directory a line was read out of already answers it, and a field inside the payload
    /// would be a claim a reader cannot check — able, therefore, to disagree with the directory.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Events))]
    public void NoPayloadNamesTheProducer(string type, Type payload)
    {
        Assert.False(typeof(ServiceEventData).IsAssignableFrom(payload),
            $"{type} derives from ServiceEventData, which requires a leaf id — the producer comes from "
            + "the journal directory, and a second answer could disagree with it");

        foreach (PropertyInfo property in Declared(payload))
        {
            Assert.False(
                property.Name is "Leaf" or "Producer" or "ProducerVersion" or "Service",
                $"{type}.{property.Name} names the producer, which the envelope already carries");
        }
    }

    /// <summary>
    /// A decision is not filed under the server it names.
    /// </summary>
    /// <remarks>
    /// It is something this host <em>noticed</em> about a server rather than something that happened
    /// to one, and its subject is not always a server at all — a threshold episode is about a sensor.
    /// A consumer routing on the subject would put a judgment in the same list as the events it judged.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Events))]
    public void AJudgmentIsNotAnInstanceEvent(string type, Type payload)
    {
        Assert.Equal(EventSubject.Service, KgsmEventCatalog.Describe(type).Subject);
        Assert.False(typeof(EventDataBase).IsAssignableFrom(payload),
            $"{type} derives from the instance-scoped payload base, which files it under a server");
    }

    /// <summary>
    /// Who shaped a rule is classified as naming a person.
    /// </summary>
    /// <remarks>
    /// The one personal field the reactor writes. A surface listing decisions to a room of players
    /// would otherwise print the operator who wrote each rule beside it — and the catalog is where a
    /// consumer learns that without having to know what a KGSM username is.
    /// </remarks>
    [Fact]
    public void WhoShapedARuleIsClassifiedAsNamingAPerson()
    {
        EventField author = Assert.Single(
            KgsmEventCatalog.Describe(ReactorEvents.Decided).Fields,
            f => f.Name == ReactorEventFields.RuleAuthor);

        Assert.Equal(FieldSensitivity.Personal, author.Sensitivity);
    }

    /// <summary>
    /// Who answered a proposal is classified as naming a person; the handle they answered with is not.
    /// </summary>
    /// <remarks>
    /// <b>The two fields pull in opposite directions and sit on the same event.</b> An answer names a
    /// natural person, so a surface has to be able to withhold it. A handle names nobody and is instead
    /// the string that lets its holder ask for the action — classified opaque so nothing renders it for
    /// want of meaning, which is also what keeps it out of a channel a fleet reads.
    /// </remarks>
    [Fact]
    public void AnAnswerNamesAPersonAndAHandleNamesNobody()
    {
        EventField answered = Assert.Single(
            KgsmEventCatalog.Describe(ReactorEvents.Resolved).Fields,
            f => f.Name == ReactorEventFields.AnsweredBy);

        Assert.Equal(FieldSensitivity.Personal, answered.Sensitivity);

        foreach (string type in new[] { ReactorEvents.Proposed, ReactorEvents.Resolved })
        {
            EventField handle = Assert.Single(
                KgsmEventCatalog.Describe(type).Fields,
                f => f.Name == ReactorEventFields.ProposalHandle);

            Assert.Equal(FieldShape.Opaque, handle.Shape);
            Assert.Equal(FieldSensitivity.Public, handle.Sensitivity);
        }
    }

    /// <summary>
    /// A proposal's handle is spelled apart from the one an account carries.
    /// </summary>
    /// <remarks>
    /// <b>A bare <c>Handle</c> already means a person on this host.</b> One field name cannot carry
    /// two classifications, so a consumer meeting both would treat whichever it saw first as the answer
    /// for both — reading a redemption token as somebody's name, or the reverse.
    /// </remarks>
    [Fact]
    public void AProposalsHandleIsNotSpelledLikeAnAccountsHandle()
    {
        Assert.NotEqual("Handle", ReactorEventFields.ProposalHandle);

        IEnumerable<EventField> elsewhere = KgsmEventCatalog.All
            .Where(d => !d.Type.StartsWith(ReactorEvents.Prefix, StringComparison.Ordinal))
            .SelectMany(d => d.Fields);

        Assert.DoesNotContain(elsewhere, f => f.Name == ReactorEventFields.ProposalHandle);
    }

    /// <summary>
    /// The four resolutions are spelled the way the leaf writes them, and exhaust the ways out.
    /// </summary>
    /// <remarks>
    /// Lower case with an underscore, matching the leaf's own conversion of its enum names — a
    /// consumer comparing against a C# name would match nothing, and one folding the last three
    /// together would lose the only signal separating a rule nobody wants from one that speaks too
    /// early.
    /// </remarks>
    [Fact]
    public void TheResolutionsAreSpelledTheWayTheLeafWritesThem()
    {
        Assert.All(ReactorResolutions.All, r => Assert.Equal(r.ToLowerInvariant(), r));
        Assert.Equal(
            ReactorResolutions.All.Count,
            ReactorResolutions.All.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            new[]
            {
                ReactorResolutions.Confirmed, ReactorResolutions.Dismissed,
                ReactorResolutions.Lapsed, ReactorResolutions.NoLongerApplicable,
            },
            ReactorResolutions.All);
    }

    /// <summary>
    /// A confirmed proposal whose action failed is representable, and so is one where none ran.
    /// </summary>
    /// <remarks>
    /// <b><c>Ok</c> is nullable and that is load-bearing.</b> Three of the four resolutions attempt
    /// nothing, and a consumer reading a missing <c>Ok</c> as <c>false</c> would report every dismissal
    /// as a broken action. The resolution says what the person did; <c>Ok</c> says what the action did.
    /// </remarks>
    [Fact]
    public void WhatThePersonDidAndWhatTheActionDidAreSeparateAnswers()
    {
        const string dismissed = """
        {"Rule":"update_regression","Subject":"necesse","Action":"propose_restore",
         "ActionInstance":"necesse","DecisionId":"9f1c","ProposalHandle":"0f2a",
         "Resolution":"dismissed","AnsweredBy":"local:claude"}
        """;

        ReactorResolvedEventData? no = JsonSerializer.Deserialize<ReactorResolvedEventData>(dismissed);
        Assert.NotNull(no);
        Assert.Equal(ReactorResolutions.Dismissed, no.Resolution);
        Assert.Null(no.Ok);

        const string failed = """
        {"Rule":"update_regression","Subject":"necesse","Action":"propose_restore",
         "ActionInstance":"necesse","DecisionId":"9f1c","ProposalHandle":"0f2a",
         "Resolution":"confirmed","AnsweredBy":"local:claude","Ok":false,
         "Detail":"no archive on record carries a pre-update reason"}
        """;

        ReactorResolvedEventData? ran = JsonSerializer.Deserialize<ReactorResolvedEventData>(failed);
        Assert.NotNull(ran);
        Assert.Equal(ReactorResolutions.Confirmed, ran.Resolution);
        Assert.False(ran.Ok);
        Assert.Equal("local:claude", ran.AnsweredBy);
    }

    /// <summary>Every event is recognised, and the family shares one prefix.</summary>
    [Fact]
    public void EveryEventIsClassifiedUnderOnePrefix()
    {
        Assert.StartsWith(ReactorEvents.Prefix, ReactorEvents.Decided, StringComparison.Ordinal);
        Assert.StartsWith(ReactorEvents.Prefix, ReactorEvents.Proposed, StringComparison.Ordinal);
        Assert.StartsWith(ReactorEvents.Prefix, ReactorEvents.Resolved, StringComparison.Ordinal);
        Assert.StartsWith(ReactorEvents.Prefix, ReactorEvents.Acted, StringComparison.Ordinal);

        Assert.Equal(ReactorEvents.Decided, KgsmEventCatalog.NameOf<ReactorDecidedEventData>());
        Assert.Equal(ReactorEvents.Proposed, KgsmEventCatalog.NameOf<ReactorProposedEventData>());
        Assert.Equal(ReactorEvents.Resolved, KgsmEventCatalog.NameOf<ReactorResolvedEventData>());
        Assert.Equal(ReactorEvents.Acted, KgsmEventCatalog.NameOf<ReactorActedEventData>());
    }

    /// <summary>The properties an event declares itself, minus what the envelope populates.</summary>
    private static IEnumerable<PropertyInfo> Declared(Type payload) =>
        payload.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !EnvelopeProperties.Contains(p.Name));

    /// <summary>A value for <paramref name="field"/> that differs from the property's initial value.</summary>
    private static object ValueFor(Type payload, string field)
    {
        PropertyInfo? property = Declared(payload)
            .FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == field
                || p.Name == field);

        Type type = property?.PropertyType ?? typeof(string);
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(long) || type == typeof(int)) return 42;
        if (type == typeof(bool)) return true;
        if (type == typeof(DateTimeOffset)) return "2026-08-28T06:26:20.0571388+00:00";
        return "a-value";
    }
}
