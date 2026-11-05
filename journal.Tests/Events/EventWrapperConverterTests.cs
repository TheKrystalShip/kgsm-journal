using System.Text.Json;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Events;

/// <summary>The envelope's payload is the registered type its event names, whatever order the line writes it in.</summary>
public class EventWrapperConverterTests
{
    private static EventWrapper Read(string line) =>
        JsonSerializer.Deserialize(line, JournalJsonContext.Default.EventWrapper)!;

    [Fact]
    public void Payload_IsTheTypeTheCatalogNamesForTheEvent()
    {
        EventWrapper wrapper = Read("""{"EventType":"server.started","Data":{"InstanceName":"7dtd"},"Hostname":"hotrod"}""");

        Assert.Equal("7dtd", Assert.IsType<InstanceStartedData>(wrapper.Data).InstanceName);
        Assert.Equal("hotrod", wrapper.Hostname);
    }

    [Fact]
    public void Payload_BeforeTheType_IsStillTheTypeTheEventNames()
    {
        EventWrapper wrapper = Read("""{"Data":{"InstanceName":"7dtd"},"EventType":"server.started"}""");

        Assert.Equal("7dtd", Assert.IsType<InstanceStartedData>(wrapper.Data).InstanceName);
    }

    [Fact]
    public void AnEventTheCatalogDoesNotClassify_CarriesTheNamesAReaderFiltersOn()
    {
        EventWrapper wrapper = Read("""{"EventType":"nobody.knows.this","Data":{"InstanceName":"7dtd","Secret":"x"}}""");

        UnrecognizedEventData data = Assert.IsType<UnrecognizedEventData>(wrapper.Data);
        Assert.Equal("7dtd", data.InstanceName);
        Assert.Null(data.BlueprintName);
    }

    [Fact]
    public void AnEventTheCatalogDoesNotClassify_StillNamesItsSubject()
    {
        EventWrapper wrapper = Read(
            """{"EventType":"host.threshold.future","Data":{"ServerId":"s1","Ref":"cpu0","Metric":"temp","Subject":"x","SubjectKind":"instance"}}""");

        UnrecognizedEventData data = Assert.IsType<UnrecognizedEventData>(wrapper.Data);
        Assert.Equal(("s1", "cpu0", "temp", "x", "instance"), (data.ServerId, data.Ref, data.Metric, data.Subject, data.SubjectKind));
    }

    [Fact]
    public void ALineWithNoPayload_HasNoData()
    {
        Assert.Null(Read("""{"EventType":"server.started","Data":null}""").Data);
        Assert.Null(Read("""{"EventType":"server.started"}""").Data);
    }

    [Fact]
    public void TheEnvelopeFields_AreReadWithoutRegardToCase_AndUnknownOnesAreSkipped()
    {
        EventWrapper wrapper = Read(
            """{"id":"abc","eventtype":"server.started","Future":{"a":[1,2]},"During":["op1"],"V":2,"Timestamp":"2026-06-14T15:39:58Z"}""");

        Assert.Equal("abc", wrapper.Id);
        Assert.Equal("server.started", wrapper.EventType);
        Assert.Equal(["op1"], wrapper.During);
        Assert.Equal(2, wrapper.SchemaVersion);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 15, 39, 58, TimeSpan.Zero), wrapper.Timestamp);
    }

    [Fact]
    public void AWrongTypedEnvelopeField_IsAJsonException()
    {
        Assert.Throws<JsonException>(() => Read("""{"EventType":7}"""));
        Assert.Throws<JsonException>(() => Read("""{"EventType":"x","Timestamp":"yesterday"}"""));
        Assert.Throws<JsonException>(() => Read("""{"EventType":"x","V":"2"}"""));
    }

    [Fact]
    public void Written_ItReadsBackAsTheSamePayload()
    {
        var wrapper = new EventWrapper
        {
            EventType = "server.started",
            Data = new InstanceStartedData { InstanceName = "7dtd" },
            Actor = "heisen",
            During = ["op1"],
        };

        EventWrapper back = Read(JsonSerializer.Serialize(wrapper, JournalJsonContext.Default.EventWrapper));

        Assert.Equal("7dtd", Assert.IsType<InstanceStartedData>(back.Data).InstanceName);
        Assert.Equal("heisen", back.Actor);
        Assert.Equal(["op1"], back.During);
    }

    [Fact]
    public void ScalarText_ReadsAStringAndANumberOffAPayload_AndNothingElse()
    {
        var data = new InstanceStartedData { InstanceName = "7dtd" };

        Assert.Equal("7dtd", EventPayloads.ScalarText(data, "InstanceName"));
        Assert.Null(EventPayloads.ScalarText(data, "NoSuchField"));
        Assert.Null(EventPayloads.ScalarText(null, "InstanceName"));
    }

    [Fact]
    public void EveryPayloadTheCatalogNames_IsRegistered_SoAnyEventReads()
    {
        foreach (EventDescriptor descriptor in KgsmEventCatalog.All.Where(d => d.PayloadType is not null))
            Assert.NotNull(JournalJsonContext.Default.GetTypeInfo(descriptor.PayloadType!));
    }
}
