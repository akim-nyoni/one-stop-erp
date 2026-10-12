using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers tenants, occupiers and managing agents of a property, the separate correspondence address and who a bill goes to.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PropertyPartyTests
{
    private readonly AccountingFixture _fixture;

    public PropertyPartyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tenants_occupiers_and_agents_are_kept_and_copied_on_bills()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer owner = new(companyId, "PP-OWN", "Stand Owner", Scenario.Usd) { Email = "owner@example.com" };
        Customer agentAccount = new(companyId, "PP-AGT", "Acme Property Managers", Scenario.Usd);
        db.Customers.AddRange(owner, agentAccount);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "PP-1001", "Ward 1", owner.Id) { Address = "1001 Main Road" };
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IOwnershipService ownership = scope.ServiceProvider.GetRequiredService<IOwnershipService>();
        DateOnly start = new(2026, 1, 1);
        long tenant = (await ownership.AddPartyAsync(companyId, stand.Id, new NewPropertyParty(PropertyPartyRole.Tenant, "Grace Dube", start) { Phone = "0772000000" })).Value;
        (await ownership.AddPartyAsync(companyId, stand.Id, new NewPropertyParty(PropertyPartyRole.ManagingAgent, "Acme", start) { ReceivesBills = true }))
            .Error.Code.Should().Be("Municipal.PartyAddress");
        (await ownership.AddPartyAsync(companyId, stand.Id, new NewPropertyParty(PropertyPartyRole.ManagingAgent, "Acme", start) { CustomerCode = "NOPE", Email = "a@b.c" }))
            .Error.Code.Should().Be("Municipal.CustomerNotFound");
        (await ownership.AddPartyAsync(companyId, stand.Id, new NewPropertyParty(PropertyPartyRole.ManagingAgent, "Acme Property Managers", start)
        {
            CustomerCode = "pp-agt", Address = "PO Box 77", Email = "agents@example.com", ReceivesBills = true,
        })).IsSuccess.Should().BeTrue();

        (await ownership.EndPartyAsync(companyId, tenant, new DateOnly(2025, 12, 1))).Error.Code.Should().Be("Municipal.PartyEnded");
        (await ownership.EndPartyAsync(companyId, tenant, new DateOnly(2026, 6, 30))).IsSuccess.Should().BeTrue();
        (await ownership.PartiesAsync(companyId, stand.Id, new DateOnly(2026, 3, 1))).Should().HaveCount(2);
        IReadOnlyList<PropertyPartyEntry> july = await ownership.PartiesAsync(companyId, stand.Id, new DateOnly(2026, 7, 1));
        july.Should().ContainSingle().Which.CustomerCode.Should().Be("PP-AGT");
        (await ownership.PartiesAsync(companyId, stand.Id, null)).Should().HaveCount(2);

        // The bill goes to the ratepayer at the property until a correspondence address is set, and a copy to the agent.
        (await ownership.BillRecipientsAsync(companyId, stand.Id, new DateOnly(2026, 7, 1))).Value[0].Address.Should().Be("1001 Main Road");
        (await ownership.SetCorrespondenceAddressAsync(companyId, stand.Id, "PO Box 12, Harare")).IsSuccess.Should().BeTrue();
        IReadOnlyList<BillRecipient> recipients = (await ownership.BillRecipientsAsync(companyId, stand.Id, new DateOnly(2026, 7, 1))).Value;
        recipients.Select(r => (r.Capacity, r.Name, r.Address)).Should().Equal(("Ratepayer", "Stand Owner", "PO Box 12, Harare"), ("Managing agent", "Acme Property Managers", "PO Box 77"));

        PropertyEnquiry enquiry = (await scope.ServiceProvider.GetRequiredService<IPropertyEnquiryService>().EnquireAsync(companyId, stand.Id, new DateOnly(2026, 3, 1))).Value;
        enquiry.Parties.Select(p => p.Role).Should().Equal(PropertyPartyRole.Tenant, PropertyPartyRole.ManagingAgent);
        enquiry.CorrespondenceAddress.Should().Be("PO Box 12, Harare");
    }
}
