using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Events;

/// <summary>
/// Binds the assistant's payload classes to the field names its emitter writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The catalog's own drift tests cannot see this.</b> They compare a descriptor's fields against
/// the payload's <em>C# property names</em>, which is the right question for classification and the
/// wrong one for binding: a property whose <see cref="JsonPropertyNameAttribute"/> drifts from the
/// constant keeps its C# name, stays classified, and silently reads back as its default at runtime.
/// </para>
/// <para>
/// So the names live once in <see cref="AssistantEventFields"/> and are checked here against a
/// document written from those constants — never against a list in this file, which would be another
/// spelling able to drift with the rest.
/// </para>
/// </remarks>
public sealed class AssistantEventContractTests
{
    /// <summary>
    /// Envelope-level properties the reader populates from the envelope rather than the payload. A
    /// payload never writes them, so they are not part of what these events declare.
    /// </summary>
    private static readonly HashSet<string> EnvelopeProperties =
        new(["Timestamp", "Actor", "Origin"], StringComparer.Ordinal);

    /// <summary>Every event the assistant produces, with the class its payload reads back into.</summary>
    public static TheoryData<string, Type> Events => new()
    {
        { AssistantEvents.ClaimCorrected, typeof(AssistantClaimCorrectedEventData) },
        { AssistantEvents.ActionDeclined, typeof(AssistantActionDeclinedEventData) },
        { AssistantEvents.ActionProposed, typeof(AssistantActionProposedEventData) },
        { AssistantEvents.BlueprintAuthoringStarted, typeof(AssistantBlueprintAuthoringStartedEventData) },
        { AssistantEvents.BlueprintAuthored, typeof(AssistantBlueprintAuthoredEventData) },
    };

    /// <summary>
    /// Every payload property binds to the wire name the catalog classifies it under.
    /// </summary>
    /// <remarks>
    /// Compared against a <b>fresh instance</b> rather than <c>default(T)</c>. A string property
    /// initialised to <see cref="string.Empty"/> is not null when it fails to bind, so a null check
    /// would pass on exactly the drift this exists to catch.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Events))]
    public void EveryPayloadPropertyBindsToTheNameTheCatalogClassifies(string type, Type payload)
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe(type);
        Assert.True(descriptor.Known, $"{type} is not classified");

        // A document written the way the emitter writes one: the catalog's field names, each with a
        // value distinguishable from the property's default.
        var document = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (EventField field in descriptor.Fields)
            document[field.Name] = ValueFor(payload, field.Name);

        // The subject a blueprint event carries structurally, which the catalog does not classify.
        if (typeof(BlueprintEventDataBase).IsAssignableFrom(payload))
            document["BlueprintName"] = "a-blueprint";

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
            + "payload's [JsonPropertyName] has drifted from AssistantEventFields: "
            + string.Join(", ", unbound.Order(StringComparer.Ordinal)));
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
    /// A staged action's handle never reaches the journal.
    /// </summary>
    /// <remarks>
    /// The handle <b>is</b> the capability that redeems the action. A journal is world-readable to
    /// anything that can open the directory, which would make writing one there a way to approve
    /// somebody else's mutation by reading a file.
    /// </remarks>
    [Fact]
    public void TheProposalCarriesNoHandle()
    {
        foreach (PropertyInfo property in Declared(typeof(AssistantActionProposedEventData)))
        {
            Assert.False(
                property.Name.Contains("Handle", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase),
                $"AssistantActionProposedEventData.{property.Name} looks like the confirmation handle, "
                + "which is the capability that redeems the action and must not be journalled");
        }
    }

    /// <summary>The properties an event declares itself, minus what the envelope populates.</summary>
    private static IEnumerable<PropertyInfo> Declared(Type payload) =>
        payload.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !EnvelopeProperties.Contains(p.Name))
            .Where(p => p.DeclaringType != typeof(BlueprintEventDataBase));

    /// <summary>A value for <paramref name="field"/> that differs from the property's initial value.</summary>
    private static object ValueFor(Type payload, string field)
    {
        PropertyInfo? property = Declared(payload)
            .FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == field
                || p.Name == field);

        Type type = property?.PropertyType ?? typeof(string);
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(long) || type == typeof(int) ? 42 : "a-value";
    }
}
