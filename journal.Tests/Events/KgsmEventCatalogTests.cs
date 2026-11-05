using System.Reflection;

using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Events;

/// <summary>
/// The two checks that keep the catalog true, and the properties consumers are allowed to rely on.
/// </summary>
/// <remarks>
/// <para>
/// The catalog exists because every surface that renders the journal was working out what each event
/// means for itself, and they drifted. A catalog that drifts from the engine is worse than none — it
/// is the same guesswork with an authoritative-looking home — so the two tests below are the point of
/// the whole exercise: <b>a new event type cannot be added to this library without being classified,
/// and a new payload field cannot be added without being classified.</b> Each fails this build at the
/// only moment anybody is thinking about that event.
/// </para>
/// <para>
/// Reflection here is deliberate and stays here. The library is embedded by Native-AOT consumers and
/// is reflection-free at runtime; a test project is neither.
/// </para>
/// </remarks>
public class KgsmEventCatalogTests
{
    /// <summary>
    /// The subject and envelope metadata every event carries. Excluded from the field classification
    /// because they are structural — the subject is <see cref="EventDescriptor.Subject"/>, and actor,
    /// origin and timestamp are the envelope's, identical on every event and never part of a payload.
    /// </summary>
    private static readonly Type[] StructuralBases =
    [
        typeof(EventDataBase),
        typeof(BlueprintEventDataBase),
        typeof(LibraryEventDataBase),
        typeof(KgsmEventDataBase),
    ];

    /// <summary>
    /// Every classified event beside the class its payload deserializes into. One source, because the
    /// catalog <em>is</em> the dispatch registry — <see cref="EventService"/> reads
    /// <see cref="EventDescriptor.PayloadType"/> to decide what to deserialize.
    /// </summary>
    private static IEnumerable<(string Type, Type Data)> TypedEvents =>
        KgsmEventCatalog.All
            .Where(d => d.PayloadType is not null)
            .Select(d => (d.Type, Data: d.PayloadType!));

    /// <summary>
    /// Every property this event declares of its own, walking up through any intermediate base (the
    /// moderation events carry their fields on a shared one) but stopping before the structural bases.
    /// </summary>
    private static IEnumerable<PropertyInfo> PayloadProperties(Type dataType)
    {
        for (Type? t = dataType; t is not null && !StructuralBases.Contains(t); t = t.BaseType)
        {
            foreach (PropertyInfo p in t.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                yield return p;
            }
        }
    }

    /// <summary>
    /// <b>An event that can be deserialized is one that has been classified — by construction.</b>
    /// There is no separate dispatch table to fall out of step with: a descriptor carries the payload
    /// type, and that is what <see cref="EventService"/> deserializes into, so the check this replaces
    /// (comparing the two registries) can no longer fail. What is still worth asserting is the shape
    /// that makes it true — every known descriptor names a payload, and an unknown one names none,
    /// which is what sends an unrecognised event down the raw-envelope path instead of a typed one.
    /// </summary>
    [Fact]
    public void AClassifiedEventNamesItsPayloadAndAnUnknownOneNamesNothing()
    {
        string[] unnamed = [.. KgsmEventCatalog.All
            .Where(d => d.PayloadType is null)
            .Select(d => d.Type)
            .OrderBy(type => type, StringComparer.Ordinal)];

        Assert.True(unnamed.Length == 0,
            $"these classified events would deserialize into nothing: {string.Join(", ", unnamed)}");

        Assert.Null(KgsmEventCatalog.Describe("instance_teleported_sideways").PayloadType);
    }

    /// <summary>
    /// <b>Drift check two, and the one that matters most.</b> A payload field nobody has classified is
    /// a field a generic renderer either prints blindly or drops silently. This is how a player's
    /// network address came to be shown on one surface and refused on another, and it is how a
    /// credential would reach a channel.
    /// </summary>
    [Fact]
    public void EveryPayloadFieldIsClassified()
    {
        List<string> unclassified = [];

        foreach ((string type, Type dataType) in TypedEvents)
        {
            EventDescriptor descriptor = KgsmEventCatalog.Describe(type);

            foreach (PropertyInfo property in PayloadProperties(dataType))
            {
                if (descriptor.Field(property.Name) is null)
                    unclassified.Add($"{type}.{property.Name}");
            }
        }

        Assert.True(unclassified.Count == 0,
            "these payload fields carry data no consumer has been told how to treat — classify them " +
            $"in KgsmEventCatalog: {string.Join(", ", unclassified.Order(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// The mirror of the check above: a descriptor naming a field the payload does not have sends a
    /// consumer looking for something that is never there, and would survive the field being renamed
    /// out from under it.
    /// </summary>
    [Fact]
    public void NoDescriptorNamesAFieldThePayloadDoesNotHave()
    {
        List<string> phantom = [];

        foreach ((string type, Type dataType) in TypedEvents)
        {
            HashSet<string> actual = [.. PayloadProperties(dataType).Select(p => p.Name)];

            foreach (EventField field in KgsmEventCatalog.Describe(type).Fields)
            {
                if (!actual.Contains(field.Name))
                    phantom.Add($"{type}.{field.Name}");
            }
        }

        Assert.True(phantom.Count == 0,
            $"these classified fields do not exist on their event's payload: {string.Join(", ", phantom)}");
    }

    /// <summary>
    /// One field name means one thing everywhere. Two events classifying <c>Command</c> differently is
    /// the same divergence one level down, and it would be invisible.
    /// </summary>
    [Fact]
    public void AFieldNameIsClassifiedTheSameWayOnEveryEvent()
    {
        var disagreements = KgsmEventCatalog.All
            .SelectMany(d => d.Fields)
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Where(g => g.Select(f => (f.Sensitivity, f.Shape)).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(disagreements.Count == 0,
            $"these fields are classified inconsistently across events: {string.Join(", ", disagreements)}");
    }

    /// <summary>
    /// An unrecognised type must come back describable rather than absent — a consumer that has to
    /// distinguish "no descriptor" from "a descriptor saying nothing" writes the null branch itself,
    /// which is the guesswork this replaces.
    /// </summary>
    [Fact]
    public void AnUnrecognisedTypeIsStillDescribed()
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe("instance_teleported_sideways");

        Assert.False(descriptor.Known);
        Assert.Equal("instance_teleported_sideways", descriptor.Type);
        Assert.Equal(EventSubject.Instance, descriptor.Subject);
    }

    /// <summary>
    /// <b>An unknown event carries no classified fields, and that means "print nothing from the
    /// payload".</b> It is the fail-safe half of the field classification: an event nobody has looked
    /// at may carry anything at all, and a renderer that reaches into it is one engine release away
    /// from publishing something it should not.
    /// </summary>
    [Fact]
    public void AnUnrecognisedTypeExposesNoFields()
    {
        Assert.Empty(KgsmEventCatalog.Describe("instance_teleported_sideways").Fields);
    }

    /// <summary>
    /// The subject is read off the engine's naming convention so that every consumer does not write
    /// that same derivation itself. It is a reading of what the engine said, not a claim — which is
    /// what <see cref="EventDescriptor.Known"/> being false says.
    /// </summary>
    [Fact]
    public void AnUnrecognisedBlueprintEventIsNotReadAsBeingAboutAServer()
    {
        Assert.Equal(EventSubject.Blueprint, KgsmEventCatalog.Describe("blueprint.reticulated").Subject);
    }

    /// <summary>
    /// A failure is always a fact. A step that did not happen is precisely what somebody reading a
    /// history back is looking for, and classifying one as a phase invites every surface that hides
    /// phases to hide it.
    /// </summary>
    [Fact]
    public void NoFailureIsClassifiedAsAPhase()
    {
        string[] hidden = [.. KgsmEventCatalog.All
            .Where(d => d.Outcome == EventOutcome.Failure && d.Weight == EventWeight.Phase)
            .Select(d => d.Type)];

        Assert.True(hidden.Length == 0, $"failures classified as phase signals: {string.Join(", ", hidden)}");
    }

    /// <summary>
    /// The classification a surface is most likely to get wrong on its own, pinned by name. Each of
    /// these is a decision somebody made for a reason, and a silent flip would change what two
    /// independent surfaces show without either of them being touched.
    /// </summary>
    [Theory]
    // The moment players can actually connect — which instance_started does not report; that one says
    // the process launched. Two facts about two different moments.
    [InlineData("server.ready", EventWeight.Fact)]
    // Brackets around an operation whose own event is the news.
    [InlineData("server.stop.started", EventWeight.Phase)]
    [InlineData("server.stop.finished", EventWeight.Phase)]
    [InlineData("server.stopped", EventWeight.Fact)]
    // The middle of a restart is a step inside one operation, not a shutdown somebody asked for —
    // which is the whole reason it is not instance_stopped. Flipping it to Fact would put a "server
    // stopped" row and a "went offline" notification in the middle of every restart.
    [InlineData("server.restart.stopped", EventWeight.Phase)]
    // A router forward and a host firewall rule are facts about different machines, not steps.
    [InlineData("network.upnp.reasserted", EventWeight.Fact)]
    public void ContestedWeightsAreWhatTheyWereDecidedToBe(string type, EventWeight expected)
    {
        Assert.Equal(expected, KgsmEventCatalog.Describe(type).Weight);
    }

    /// <summary>
    /// The three fields that are not plain public data, pinned by name and by reason. A surface may
    /// decide what to do with each — that is the whole design — but none of them may quietly become
    /// <see cref="FieldSensitivity.Public"/>, because every consumer's handling keys off this.
    /// </summary>
    [Fact]
    public void TheFieldsThatNeedCareStaySoClassified()
    {
        // Where somebody connected from. It identifies a person rather than a player, and the game
        // shows it to nobody.
        Assert.Equal(FieldSensitivity.Personal,
            KgsmEventCatalog.Describe("player.joined").Field("PlayerAddr")!.Sensitivity);
        Assert.Equal(FieldSensitivity.Personal,
            KgsmEventCatalog.Describe("player.left").Field("PlayerAddr")!.Sensitivity);

        // May be an address, a name or an id — the blueprint declares which, and the event does not
        // carry that. Nothing here may resolve it on a consumer's behalf.
        Assert.Equal(FieldSensitivity.Conditional,
            KgsmEventCatalog.Describe("player.banned").Field("Target")!.Sensitivity);

        // Admin-level by nature: a console command can create an operator or carry a token.
        Assert.Equal(FieldSensitivity.Privileged,
            KgsmEventCatalog.Describe("console.input.sent").Field("Command")!.Sensitivity);
    }

    /// <summary>
    /// The canonical port list is structured, and a generic renderer flattening it puts JSON in a
    /// sentence. Marked by shape so a consumer skips it without having to know what "Ports" means.
    /// </summary>
    [Fact]
    public void StructuredAndMeaninglessFieldsAreMarkedByShape()
    {
        Assert.Equal(FieldShape.Ports, KgsmEventCatalog.Describe("network.ports.opened").Field("Ports")!.Shape);

        // The supervisor's correlation token: public — it says nothing about anybody — but meaningless
        // to a reader, so nothing renders it for want of meaning rather than for privacy.
        EventField session = KgsmEventCatalog.Describe("player.joined").Field("SessionKey")!;
        Assert.Equal(FieldShape.Opaque, session.Shape);
        Assert.Equal(FieldSensitivity.Public, session.Sensitivity);
    }

    /// <summary>
    /// The checks above walk the catalog, so an empty one would pass every last of them.
    /// </summary>
    [Fact]
    public void TheCatalogIsNotVacuouslyEmpty()
    {
        Assert.NotEmpty(TypedEvents);
        Assert.Equal(TypedEvents.Count(), KgsmEventCatalog.All.Count);
    }

    [Fact]
    public void Threshold_events_are_host_scoped_facts()
    {
        // Host, not Instance: a threshold episode may name the server it is about, but it is the host's
        // monitoring that established it, and most episodes name no server at all.
        EventDescriptor breach = KgsmEventCatalog.Describe("host.threshold.breached");
        EventDescriptor cleared = KgsmEventCatalog.Describe("host.threshold.cleared");

        Assert.True(breach.Known);
        Assert.True(cleared.Known);
        Assert.Equal(EventSubject.Host, breach.Subject);
        Assert.Equal(EventSubject.Host, cleared.Subject);

        // Two immutable facts, not one row that changes — the journal is append-only, and the mutable
        // view of the same condition is the alert feed, which answers a different question.
        Assert.Equal(EventWeight.Fact, breach.Weight);
        Assert.Equal(EventWeight.Fact, cleared.Weight);

        // Neither reports a failure. A value crossing a line is a measurement; how loudly to say so is
        // the reading surface's business, not the catalog's.
        Assert.Equal(EventOutcome.Neutral, breach.Outcome);
        Assert.Equal(EventOutcome.Neutral, cleared.Outcome);
    }

    [Fact]
    public void A_cleared_episode_carries_why_it_ended()
    {
        // Load-bearing: an episode that ended because its rule was retuned, disabled or removed did not
        // recover — the value was never observed to come down. A consumer that cannot see the reason
        // cannot avoid reporting a measurement nobody took.
        EventDescriptor cleared = KgsmEventCatalog.Describe("host.threshold.cleared");

        Assert.Contains(cleared.Fields, f => f.Name == "CloseReason");
        Assert.Contains(cleared.Fields, f => f.Name == "ClosedTs");
        // The open moment travels on both, so a reader can place the breach without holding the pair.
        Assert.Contains(cleared.Fields, f => f.Name == "OpenedTs");
    }

    [Theory]
    [InlineData("auth.signed_in", "Identity")]
    [InlineData("auth.signed_in", "UserAgent")]
    [InlineData("identity.linked", "Handle")]
    [InlineData("identity.unlinked", "Handle")]
    public void An_account_event_marks_what_identifies_a_person(string type, string field)
    {
        // These are the fields that link this host's account to somebody outside it, or describe the
        // machine they used. Every surface reads its "may I show this" answer off this classification,
        // so a field demoted to Public here becomes visible on every one of them at once.
        EventField? classified = KgsmEventCatalog.Describe(type).Field(field);

        Assert.NotNull(classified);
        Assert.Equal(FieldSensitivity.Personal, classified.Sensitivity);
    }

    [Fact]
    public void An_account_event_still_says_who_it_was_about()
    {
        // The counterweight to the rule above: withholding the username too would leave a trail that
        // records accounts changing and names nobody, which is not a safer log — it is a useless one.
        EventDescriptor login = KgsmEventCatalog.Describe("auth.signed_in");

        Assert.Equal(FieldSensitivity.Public, login.Field("Username")!.Sensitivity);
        Assert.Equal(FieldSensitivity.Public, login.Field("Provider")!.Sensitivity);
    }

    [Fact]
    public void A_service_config_change_carries_keys_and_never_values()
    {
        // The one classification that would leak a credential if it were wrong: a leaf's configuration
        // holds tokens and passwords, so the descriptor has somewhere to put the keys and deliberately
        // nowhere to put what they were set to.
        EventDescriptor changed = KgsmEventCatalog.Describe("service.config_changed");

        Assert.Contains(changed.Fields, f => f.Name == "Keys");
        Assert.DoesNotContain(changed.Fields, f =>
            f.Name.Contains("Value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_panels_own_events_are_not_read_as_being_about_a_game_server()
    {
        // A login is not an instance event. Read off the engine's naming convention instead, it would
        // file every sign-in under whichever server the reader was looking at.
        Assert.Equal(EventSubject.Account, KgsmEventCatalog.Describe("auth.signed_in").Subject);
        Assert.Equal(EventSubject.Account, KgsmEventCatalog.Describe("user.approved").Subject);
        Assert.Equal(EventSubject.Service, KgsmEventCatalog.Describe("service.restarted").Subject);

        // These two genuinely are about one instance, and stay that way.
        Assert.Equal(EventSubject.Instance, KgsmEventCatalog.Describe("file.written").Subject);
        Assert.Equal(EventSubject.Instance, KgsmEventCatalog.Describe("backup.downloaded").Subject);
    }

    [Fact]
    public void A_payload_class_names_the_event_it_belongs_to()
    {
        // The derivation a typed consumer uses in place of writing the name beside its handler.
        Assert.Equal("server.started", KgsmEventCatalog.NameOf<InstanceStartedData>());
        Assert.Equal("server.crash.exhausted", KgsmEventCatalog.NameOf<InstanceFailedData>());
        Assert.Equal("host.threshold.breached", KgsmEventCatalog.NameOf<HostThresholdBreachedData>());
    }

    [Fact]
    public void Every_classified_payload_class_either_names_one_event_or_refuses()
    {
        // The property the derivation rests on: asking a class what it is called is never a guess.
        // Either it is bound to exactly one event and answers, or it is bound to several and says so.
        var byPayload = KgsmEventCatalog.All
            .Where(d => d.PayloadType is not null)
            .GroupBy(d => d.PayloadType!);

        MethodInfo nameOf = typeof(KgsmEventCatalog).GetMethod(nameof(KgsmEventCatalog.NameOf))!;

        foreach (var group in byPayload)
        {
            object? Ask() => nameOf.MakeGenericMethod(group.Key).Invoke(null, null);

            if (group.Count() == 1)
            {
                Assert.Equal(group.Single().Type, Ask());
                continue;
            }

            // Shared by several events: the producer's own constants name those, so this refuses
            // rather than returning whichever happened to be registered first.
            TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(Ask);
            Assert.IsType<InvalidOperationException>(thrown.InnerException);
        }
    }

    [Fact]
    public void A_class_the_catalog_does_not_classify_names_nothing()
    {
        Assert.Throws<InvalidOperationException>(() => KgsmEventCatalog.NameOf<UnclassifiedPayload>());
    }

    [Fact]
    public void A_server_going_down_unasked_is_danger_either_way()
    {
        // The supervisor still trying does not make the fact routine, and the two are told apart by
        // their names rather than by their weight.
        Assert.Equal(EventSeverity.Danger, KgsmEventCatalog.Describe("server.crashed").Severity);
        Assert.Equal(EventSeverity.Danger, KgsmEventCatalog.Describe("server.crash.exhausted").Severity);
    }

    private sealed class UnclassifiedPayload : EventDataBase;
}
