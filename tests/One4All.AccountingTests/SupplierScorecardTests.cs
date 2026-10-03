using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the supplier scorecard: one supplier delivers one order early and one late (50% on time), has 2 of its 20 units
/// returned (90% quality) and invoices 300; another delivers its only order on time with nothing returned; an order with
/// no required-by date is not judged for timeliness.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierScorecardTests
{
    private readonly AccountingFixture _fixture;

    public SupplierScorecardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Suppliers_are_scored_on_timeliness_quality_and_spend()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "5190", "Purchase Price Variance", AccountType.Expense));
        Supplier late = new(companyId, "SC-A", "Sometimes Late", Scenario.Usd);
        Supplier prompt = new(companyId, "SC-B", "Always Prompt", Scenario.Usd);
        Item widget = new(companyId, "SC-W", "Widget") { CostOfSalesAccountCode = "5000" };
        Warehouse wh = new(companyId, "SCWH", "Main");
        db.AddRange(late, prompt, widget, wh);
        await db.SaveChangesAsync();

        IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task Deliver(Supplier supplier, string number, DateOnly? requiredBy, DateOnly received, decimal quantity)
        {
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), RequiredBy = requiredBy, Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(widget.Id, "Widget", quantity, 5m)],
            })).Value.PurchaseOrderId;
            long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = wh.Id, GrnNumber = number + "-G", Date = received, Lines = [new ReceiveLine(lineId, quantity)],
            })).IsSuccess.Should().BeTrue();
        }

        await Deliver(late, "SC-PO1", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 8), 10m);
        await Deliver(late, "SC-PO2", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15), 8m);
        await Deliver(late, "SC-PO3", null, new DateOnly(2026, 3, 16), 2m);
        await Deliver(prompt, "SC-PO4", new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 18), 5m);
        (await pop.ReturnToSupplierAsync(companyId, late.Id, widget.Id, wh.Id, 2m, 5m, new DateOnly(2026, 3, 20), "SC-RET", "5190")).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = late.Id, DocumentNumber = "SC-INV", DocumentDate = new DateOnly(2026, 3, 25), Currency = Scenario.Usd, NetAmount = 300m, ExpenseAccountCode = "6800",
        })).IsSuccess.Should().BeTrue();

        IReadOnlyList<SupplierScore> scores = await scope.ServiceProvider.GetRequiredService<ISupplierScorecardService>()
            .ScorecardAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        scores.Should().HaveCount(2);
        scores[0].Should().BeEquivalentTo(new
        {
            SupplierCode = "SC-A", Deliveries = 3, DeliveriesJudged = 2, DeliveriesOnTime = 1, QuantityReceived = 20m, QuantityReturned = 2m, Spend = 300m,
            OnTimePercent = (decimal?)50m, QualityPercent = (decimal?)90m,
        });
        scores[1].Should().BeEquivalentTo(new { SupplierCode = "SC-B", Deliveries = 1, OnTimePercent = (decimal?)100m, QualityPercent = (decimal?)100m, Spend = 0m });
    }
}
