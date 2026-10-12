using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the request-for-quotation document: it prints the items, quantities and closing date, and emails to the order contact of each
/// invited supplier that has one, naming the supplier with nobody to email.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RfqDocumentTests
{
    private readonly AccountingFixture _fixture;

    public RfqDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_request_prints_and_emails_to_the_invited_suppliers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier withContact = new(companyId, "RFQD-A", "Has a buyer contact", Scenario.Usd);
        Supplier noContact = new(companyId, "RFQD-B", "Nobody to email", Scenario.Usd);
        Item cement = new(companyId, "RFQD-CEM", "Cement");
        db.AddRange(withContact, noContact, cement);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ITraderDirectoryService>().AddContactAsync(companyId, TradingLedger.AccountsPayable, "RFQD-A",
            new NewTraderContact("Sales desk", Email: "sales@example.test", ReceivesOrders: true))).IsSuccess.Should().BeTrue();

        IRfqService rfqs = scope.ServiceProvider.GetRequiredService<IRfqService>();
        long rfqId = (await rfqs.IssueAsync(companyId, new RfqRequest("RFQD-26-01", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15),
            [withContact.Id, noContact.Id], [new RfqLineRequest(cement.Id, "Cement 50kg", 120m)]))).Value;

        (await rfqs.RenderAsync(companyId, rfqId)).Value.Should().Contain("REQUEST FOR QUOTATION").And.Contain("RFQD-26-01").And.Contain("RFQD-CEM")
            .And.Contain("120").And.Contain("2026-03-15");

        RfqDespatch sent = (await rfqs.SendAsync(companyId, rfqId)).Value;
        sent.Recipients.Should().Be(1);
        sent.NoEmail.Should().Equal("RFQD-B");
        (await scope.ServiceProvider.GetRequiredService<INotificationService>().BatchStatusAsync(companyId, sent.BatchId!.Value)).Value.Queued.Should().Be(1);
    }
}
