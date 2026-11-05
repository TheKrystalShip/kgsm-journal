using System.Text.Json;
using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Events;

/// <summary>
/// Tests for <see cref="AuditId.ForEvent"/> — the deterministic, content-derived
/// event id both kgsm-monitor and kgsm-api compute independently. The property that
/// matters is: same content → same id, every time, with no randomness; different
/// content (in any identifying field) → a different id.
/// </summary>
public class AuditIdTests
{
    private static EventWrapper Wrapper(
        string eventType = "server.started",
        string dataJson = """{"InstanceName":"7dtd"}""",
        DateTimeOffset? timestamp = null,
        string? hostname = "hotrod") =>
        new()
        {
            EventType = eventType,
            Data = JsonSerializer.Deserialize<JsonElement>(dataJson),
            Timestamp = timestamp ?? new DateTimeOffset(2026, 6, 14, 15, 39, 58, TimeSpan.Zero),
            Hostname = hostname,
        };

    [Fact]
    public void ForEvent_SameContentTwice_ProducesIdenticalId()
    {
        string first = AuditId.ForEvent(Wrapper());
        string second = AuditId.ForEvent(Wrapper());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForEvent_StartsWithEvtPrefix()
    {
        string id = AuditId.ForEvent(Wrapper());

        Assert.StartsWith("evt_", id);
    }

    [Fact]
    public void ForEvent_DifferentData_ProducesDifferentId()
    {
        string a = AuditId.ForEvent(Wrapper(dataJson: """{"InstanceName":"7dtd"}"""));
        string b = AuditId.ForEvent(Wrapper(dataJson: """{"InstanceName":"7dtd","ExitCode":"1"}"""));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEvent_DifferentInstanceName_ProducesDifferentId()
    {
        string a = AuditId.ForEvent(Wrapper(dataJson: """{"InstanceName":"7dtd"}"""));
        string b = AuditId.ForEvent(Wrapper(dataJson: """{"InstanceName":"factorio-test"}"""));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEvent_DifferentTimestamp_ProducesDifferentId()
    {
        string a = AuditId.ForEvent(Wrapper(timestamp: new DateTimeOffset(2026, 6, 14, 15, 39, 58, TimeSpan.Zero)));
        string b = AuditId.ForEvent(Wrapper(timestamp: new DateTimeOffset(2026, 6, 14, 15, 39, 59, TimeSpan.Zero)));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEvent_DifferentEventType_ProducesDifferentId()
    {
        string a = AuditId.ForEvent(Wrapper(eventType: "server.started"));
        string b = AuditId.ForEvent(Wrapper(eventType: "server.stopped"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEvent_NullTimestamp_IsDeterministic_NotThrowing()
    {
        EventWrapper NullTimestampWrapper() => new()
        {
            EventType = "server.started",
            Data = JsonSerializer.Deserialize<JsonElement>("""{"InstanceName":"7dtd"}"""),
            Timestamp = null,
            Hostname = "hotrod",
        };

        string first = AuditId.ForEvent(NullTimestampWrapper());
        string second = AuditId.ForEvent(NullTimestampWrapper());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForEvent_NoInstanceNameInData_TreatsAsEmpty_Deterministic()
    {
        string first = AuditId.ForEvent(Wrapper(dataJson: "{}"));
        string second = AuditId.ForEvent(Wrapper(dataJson: "{}"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForEvent_UndefinedData_TreatsAsEmpty_Deterministic()
    {
        var wrapper = new EventWrapper { EventType = "server.started", Hostname = "hotrod" };

        string first = AuditId.ForEvent(wrapper);
        string second = AuditId.ForEvent(wrapper);

        Assert.Equal(first, second);
        Assert.StartsWith("evt_", first);
    }

    [Fact]
    public void ForEvent_NullHostname_ProducesDifferentIdFromNamedHost()
    {
        string a = AuditId.ForEvent(Wrapper(hostname: null));
        string b = AuditId.ForEvent(Wrapper(hostname: "hotrod"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEvent_NullWrapper_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AuditId.ForEvent(null!));
    }
}
