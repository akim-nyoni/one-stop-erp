using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget gate on orders and invoices: a direct order is checked and commits its budgeted lines, invoicing uses the
/// commitment up line by line, a price rise beyond it is checked again, cancelling frees the rest, and the reconciliation reports
/// commitments that disagree with their orders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetGateOrderTests
{
    private readonly AccountingFixture _fixture;

    public BudgetGateOrderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Orders_commit_invoices_use_up_and_cancellation_frees_budget()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        Supplier supplier = new(companyId, "BGO-SUP", "Cleaning Co", Scenario.Usd);
        Item soap = new(companyId, "BGO-SOAP", "Soap") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "BGO-WH", "Store");
        db.AddRange(supplier, soap, store);
        await db.SaveChangesAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budget = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6100", 1, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, budget)).IsSuccess.Should().BeTrue();
        IBudgetGateService gate = scope.ServiceProvider.GetRequiredService<IBudgetGateService>();
        (await gate.SetRuleAsync(companyId, null, "61", 0m, BudgetAction.Block)).IsSuccess.Should().BeTrue();
        decimal Available() => gate.CheckAsync(companyId, new DateOnly(2026, 4, 1), [new BudgetDemand("6100", null, 1m)]).Result.Value.Lines.Single().Available;

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        CreatePurchaseOrderRequest Order(string number, decimal quantity) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(soap.Id, "Soap", quantity, 10m, "6100")],
        };
        (await procurement.CreatePurchaseOrderAsync(Order("BGO-PO0", 120m))).Error.Code.Should().Be("Budget.Blocked");
        long po = (await procurement.CreatePurchaseOrderAsync(Order("BGO-PO1", 60m))).Value.PurchaseOrderId;
        Available().Should().Be(400m);

        // Receive and invoice 20 at 10: 200 of the commitment is used up.
        long line = db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po).Select(l => l.Id).Single();
        (await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = po, WarehouseId = store.Id, GrnNumber = "BGO-GRN1", Date = new DateOnly(2026, 4, 2),
            Lines = [new ReceiveLine(line, 30m)] })).IsSuccess.Should().BeTrue();
        (await procurement.MatchInvoiceAsync(new MatchInvoiceRequest { CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "BGO-INV1", Date = new DateOnly(2026, 4, 3),
            Lines = [new InvoiceMatchLine(line, 20m, 10m)] })).IsSuccess.Should().BeTrue();
        (await db.Set<BudgetCommitment>().AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Reference == "BGO-PO1")).Amount.Should().Be(400m);

        // Invoicing 10 more at a higher price: the 40 above the order is checked again and, with the last 600 reserved, blocked.
        (await gate.ReserveAsync(companyId, "BGO-RES", new DateOnly(2026, 4, 3), new BudgetDemand("6100", null, 600m))).IsSuccess.Should().BeTrue();
        (await procurement.MatchInvoiceAsync(new MatchInvoiceRequest { CompanyId = companyId, PurchaseOrderId = po, InvoiceNumber = "BGO-INV2", Date = new DateOnly(2026, 4, 4),
            TolerancePercent = 50m, PriceVarianceAccountCode = "6100", Lines = [new InvoiceMatchLine(line, 10m, 14m)] })).Error.Code.Should().Be("Budget.Blocked");
        (await gate.ReleaseAsync(companyId, "BGO-RES")).Should().Be(600m);

        // The reconciliation agrees with the order; an orphaned commitment shows up.
        (await gate.ReconcileAsync(companyId)).Should().BeEmpty();
        (await gate.CommitAsync(companyId, "BGO-PO1", new DateOnly(2026, 4, 1), [new BudgetDemand("6100", null, 5m)])).IsSuccess.Should().BeTrue();
        (await gate.ReconcileAsync(companyId)).Should().ContainSingle(e => e.Reference == "BGO-PO1" && e.Problem.StartsWith("Commitment differs"));

        // A cancelled order cannot release received goods, but a fresh one can be cancelled and frees its budget.
        long second = (await procurement.CreatePurchaseOrderAsync(Order("BGO-PO2", 10m))).Value.PurchaseOrderId;
        decimal before = Available();
        (await procurement.CancelPurchaseOrderAsync(companyId, second, "No longer needed")).IsSuccess.Should().BeTrue();
        Available().Should().Be(before + 100m);
    }
}
