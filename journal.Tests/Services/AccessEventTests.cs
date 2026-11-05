using System.Text.Json;

using TheKrystalShip.KGSM.Events;

namespace TheKrystalShip.KGSM.Tests.Services;

/// <summary>
/// The auth anchor's access events: classified in the catalog, and read back from the payloads the
/// anchor's journal writes.
/// </summary>
public class AccessEventTests
{
    [Theory]
    [InlineData("auth.assignment.granted", typeof(AssignmentEventData), EventSubject.Account)]
    [InlineData("auth.assignment.revoked", typeof(AssignmentEventData), EventSubject.Account)]
    [InlineData("auth.role.changed", typeof(AuthorityRecordEventData), EventSubject.Access)]
    [InlineData("auth.role.removed", typeof(AuthorityRecordEventData), EventSubject.Access)]
    [InlineData("auth.permission.changed", typeof(AuthorityRecordEventData), EventSubject.Access)]
    [InlineData("auth.permission.removed", typeof(AuthorityRecordEventData), EventSubject.Access)]
    [InlineData("auth.catalog.changed", typeof(CatalogEventData), EventSubject.Access)]
    [InlineData("auth.service.requirement.approved", typeof(ServiceRequirementEventData), EventSubject.Access)]
    [InlineData("auth.service.requirement.revoked", typeof(ServiceRequirementEventData), EventSubject.Access)]
    public void EveryAccessEvent_IsClassified(string type, Type payload, EventSubject subject)
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe(type);

        Assert.True(descriptor.Known);
        Assert.Equal(payload, descriptor.PayloadType);
        Assert.Equal(subject, descriptor.Subject);
        Assert.Contains(descriptor.Fields, f => f.Name == "AuthorityVersion");
    }

    [Theory]
    [InlineData("auth.application.changed", typeof(ApplicationEventData), EventSubject.Access, EventOutcome.Neutral)]
    [InlineData("auth.application.removed", typeof(ApplicationEventData), EventSubject.Access, EventOutcome.Neutral)]
    [InlineData("auth.application.client.removed", typeof(ApplicationEventData), EventSubject.Access, EventOutcome.Neutral)]
    [InlineData("auth.application.secret.rotated", typeof(ApplicationEventData), EventSubject.Access, EventOutcome.Neutral)]
    [InlineData("auth.token.exchanged", typeof(TokenExchangeEventData), EventSubject.Account, EventOutcome.Success)]
    [InlineData("auth.token.exchange_refused", typeof(TokenExchangeEventData), EventSubject.Account, EventOutcome.Failure)]
    public void EveryApplicationEvent_IsClassified(string type, Type payload, EventSubject subject, EventOutcome outcome)
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe(type);

        Assert.True(descriptor.Known);
        Assert.Equal(payload, descriptor.PayloadType);
        Assert.Equal(subject, descriptor.Subject);
        Assert.Equal(outcome, descriptor.Outcome);
    }

    [Fact]
    public void AClientChange_ReadsBackFromTheAnchorsPayload()
    {
        const string json = """
            {"Id":"cinema","Name":"Krystal Cinema","Client":"cinema-web","Actor":"local:owner","Origin":"ui"}
            """;

        var data = JsonSerializer.Deserialize(json, JournalJsonContext.Default.ApplicationEventData)!;

        Assert.Equal("cinema", data.Id);
        Assert.Equal("Krystal Cinema", data.Name);
        Assert.Equal("cinema-web", data.Client);
    }

    [Fact]
    public void ARefusedExchange_NamesNobodyWhenNoAccountHoldsTheIdentity()
    {
        const string json = """
            {"Client":"cinema-bot","Application":"cinema","Identity":"discord:1234","UserId":null,"Username":null,
             "ActedBy":"cinema-bot","Reason":"account_unknown","Actor":"discord:1234","Origin":"discord"}
            """;

        var data = JsonSerializer.Deserialize(json, JournalJsonContext.Default.TokenExchangeEventData)!;

        Assert.Equal("cinema-bot", data.Client);
        Assert.Equal("cinema", data.Application);
        Assert.Equal("discord:1234", data.Identity);
        Assert.Null(data.UserId);
        Assert.Equal("cinema-bot", data.ActedBy);
        Assert.Equal("account_unknown", data.Reason);
    }

    [Fact]
    public void AnExchange_IdentityIsPersonal()
    {
        EventDescriptor descriptor = KgsmEventCatalog.Describe("auth.token.exchanged");

        Assert.Equal(FieldSensitivity.Personal, descriptor.Fields.Single(f => f.Name == "Identity").Sensitivity);
    }

    [Fact]
    public void AnAssignment_ReadsBackFromTheAnchorsPayload()
    {
        const string json = """
            {"AssignmentId":"asg_1","UserId":"usr_1","Username":null,"RoleId":"role_1","Role":"Hosts",
             "Scope":"instance:walter/terraria#9f3c","AuthorityVersion":12,"Actor":"local:owner","Origin":"ui"}
            """;

        var data = JsonSerializer.Deserialize(json, JournalJsonContext.Default.AssignmentEventData)!;

        Assert.Equal("asg_1", data.AssignmentId);
        Assert.Equal("usr_1", data.UserId);
        Assert.Equal("Hosts", data.Role);
        Assert.Equal("instance:walter/terraria#9f3c", data.Scope);
        Assert.Equal(12, data.AuthorityVersion);
        Assert.Equal("local:owner", data.Actor);
    }

    [Fact]
    public void ACatalogChange_ReadsBackItsArrivalsAndDepartures()
    {
        const string json = """
            {"Member":"walter","Added":["reactor:rules.write"],"Removed":[],"AuthorityVersion":3,"Actor":"system:walter"}
            """;

        var data = JsonSerializer.Deserialize(json, JournalJsonContext.Default.CatalogEventData)!;

        Assert.Equal("walter", data.Member);
        Assert.Equal(["reactor:rules.write"], data.Added!);
        Assert.Empty(data.Removed!);
    }

    [Fact]
    public void ARequirement_SaysWhetherAnybodyDecidedIt()
    {
        const string json = """
            {"AccountId":"usr_svc","Service":null,"Action":"kgsm:server.restart","Scope":"node:walter","Automatic":true,"AuthorityVersion":4}
            """;

        var data = JsonSerializer.Deserialize(json, JournalJsonContext.Default.ServiceRequirementEventData)!;

        Assert.True(data.Automatic);
        Assert.Equal("node:walter", data.Scope);
        Assert.Null(data.Service);
    }
}
