using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the purchase price variance on the supplier scorecard: invoices matched above or below the order prices.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ScorecardPriceVarianceTests
{
    private readonly AccountingFixture _fixture;

    public ScorecardPriceVarianceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_scorecard_shows_what_a_supplier_charged_above_the_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "SPV-SUP", "Pricey Supplies", Scenario.Usd);
        Item paper = new(companyId, "SPV-PAPER", "Paper") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "SPV-WH", "Store");
        db.AddRange(supplier, paper, store);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        long po = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = "SPV-PO1", OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(paper.Id, "Paper", 30m, 10m, "6100")],
        })).Value.PurchaseOrderId;
        long line = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po).Select(l => l.Id).Single();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = po, WarehouseId = store.Id, GrnNumber = "SPV-GRN1", Date = new DateOnly(2026, 4, 2), Lines = [new ReceiveLine(line, 30m)],
        })).IsSuccess.Should().BeTrue();

        // 20 at the order price, then 10 at 10.50: 5 above an order value of 300.
        (await procurement.MatchInvoiceAsync(new MatchInvoiceRequest
        {
            CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "SPV-INV1", Date = new DateOnly(2026, 4, 3), Lines = [new InvoiceMatchLine(line, 20m, 10m)],
        })).IsSuccess.Should().BeTrue();
        (await procurement.MatchInvoiceAsync(new MatchInvoiceRequest
        {
            CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "SPV-INV2", Date = new DateOnly(2026, 4, 4), PriceVarianceAccountCode = "6100", Lines = [new InvoiceMatchLine(line, 10m, 10.5m)],
        })).IsSuccess.Should().BeTrue();

        SupplierScore score = (await scope.ServiceProvider.GetRequiredService<ISupplierScorecardService>().ScorecardAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)))
            .Single(s => s.SupplierId == supplier.Id);
        (score.PriceVariance, score.PriceVariancePercent).Should().Be((5m, (decimal?)1.7m), "5 above 300 is 1.7%");
    }
}
