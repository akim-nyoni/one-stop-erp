using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers requests for quotation: three suppliers are invited to quote for 10 desks and 20 chairs; one quotes
/// cheapest but slowest, one quickest, and one never quotes; an uninvited supplier and a late quote are refused; the
/// comparison ranks the quickest first on 70/30 weighting; awarding the cheaper second-ranked quote needs a reason, and
/// raises a purchase order at its prices with a required-by date from its lead time.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RfqTests
{
    private readonly AccountingFixture _fixture;

    public RfqTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Quotes_are_compared_and_the_award_becomes_a_purchase_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier cheap = new(companyId, "RFQ-CHEAP", "Budget Furniture", Scenario.Usd);
        Supplier quick = new(companyId, "RFQ-QUICK", "Express Furniture", Scenario.Usd);
        Supplier silent = new(companyId, "RFQ-SILENT", "Never Replies", Scenario.Usd);
        Supplier outsider = new(companyId, "RFQ-OUT", "Not Invited", Scenario.Usd);
        Item desk = new(companyId, "RFQ-DESK", "Desk");
        Item chair = new(companyId, "RFQ-CHAIR", "Chair");
        db.AddRange(cheap, quick, silent, outsider, desk, chair);
        await db.SaveChangesAsync();

        IRfqService rfqs = scope.ServiceProvider.GetRequiredService<IRfqService>();
        long rfqId = (await rfqs.IssueAsync(companyId, new RfqRequest("RFQ-26-01", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15),
            [cheap.Id, quick.Id, silent.Id], [new RfqLineRequest(desk.Id, "Office desk", 10m), new RfqLineRequest(chair.Id, "Office chair", 20m)]))).Value;

        Dictionary<long, decimal> Prices(decimal deskPrice, decimal chairPrice) => new() { [desk.Id] = deskPrice, [chair.Id] = chairPrice };
        (await rfqs.RecordQuoteAsync(companyId, rfqId, new QuoteRequest(cheap.Id, new DateOnly(2026, 3, 10), 30, Prices(60m, 10m)))).IsSuccess.Should().BeTrue();   // 800
        (await rfqs.RecordQuoteAsync(companyId, rfqId, new QuoteRequest(quick.Id, new DateOnly(2026, 3, 12), 10, Prices(70m, 15m)))).IsSuccess.Should().BeTrue();   // 1,000
        (await rfqs.RecordQuoteAsync(companyId, rfqId, new QuoteRequest(outsider.Id, new DateOnly(2026, 3, 12), 5, Prices(1m, 1m)))).Error.Code.Should().Be("Rfq.NotInvited");
        (await rfqs.RecordQuoteAsync(companyId, rfqId, new QuoteRequest(silent.Id, new DateOnly(2026, 3, 16), 5, Prices(1m, 1m)))).Error.Code.Should().Be("Rfq.Closed");
        (await rfqs.RecordQuoteAsync(companyId, rfqId, new QuoteRequest(silent.Id, new DateOnly(2026, 3, 14), 5, new Dictionary<long, decimal> { [desk.Id] = 1m }))).Error.Code.Should().Be("Rfq.Incomplete");

        RfqComparison comparison = (await rfqs.CompareAsync(companyId, rfqId)).Value;
        comparison.Quotes.Select(q => (q.SupplierCode, q.Score.Total, q.Score.Score, q.Score.Rank)).Should().Equal(
            ("RFQ-QUICK", 1_000m, 86m, 1),    // 56 for price + 30 for lead time
            ("RFQ-CHEAP", 800m, 80m, 2));     // 70 for price + 10 for lead time
        comparison.NotQuoted.Should().Equal(silent.Id);

        (await rfqs.AwardAsync(companyId, rfqId, cheap.Id, new DateOnly(2026, 3, 20), 7, null)).Error.Code.Should().Be("Rfq.AwardReason");
        PurchaseOrderResult order = (await rfqs.AwardAsync(companyId, rfqId, cheap.Id, new DateOnly(2026, 3, 20), 7, "Delivery date is not critical; 20% cheaper")).Value;
        order.OrderNumber.Should().Be("RFQ-26-01-PO");

        PurchaseOrder po = await db.Set<PurchaseOrder>().AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.Id == order.PurchaseOrderId);
        (po.SupplierId, po.RequiredBy, po.TotalNet).Should().Be((cheap.Id, new DateOnly(2026, 4, 19), 800m));
        (await rfqs.CompareAsync(companyId, rfqId)).Value.Should().BeEquivalentTo(new { AwardedSupplierId = (long?)cheap.Id, AwardReason = "Delivery date is not critical; 20% cheaper" });
        (await rfqs.AwardAsync(companyId, rfqId, quick.Id, new DateOnly(2026, 3, 20), 7, null)).Error.Code.Should().Be("Rfq.NotOpen");
    }
}
