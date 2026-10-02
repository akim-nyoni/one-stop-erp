using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Banking;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the period-close checklist: a clean company passes everything; unreconciled statement lines,
/// goods received but not invoiced and an approval in progress are counted (with the GRNI value), items
/// after the period end are ignored, and clearing them restores readiness.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PeriodCloseChecklistTests
{
    private readonly AccountingFixture _fixture;

    public PeriodCloseChecklistTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Checklist_counts_outstanding_items_as_at_the_period_end()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        DateOnly periodEnd = new(2026, 3, 31);

        long companyId;
        long bankAccountId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "PC-BANK", "Current", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(bank);
            Supplier supplier = new(companyId, "PC-S", "Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "PC-ITEM", "Thing") { CostOfSalesAccountCode = "5000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "PCWH", "Main");
            db.Warehouses.Add(wh);
            WorkflowDefinition definition = new(companyId, "PurchaseRequisition", "Single approval");
            definition.AddStage(1, "Buyer");
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;
            supplierId = supplier.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Clean company: everything passes.
            PeriodCloseChecklist clean = await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>().ChecklistAsync(companyId, periodEnd);
            clean.IsReadyToClose.Should().BeTrue();
            clean.Outstanding.Should().Be(0);
            clean.Checks.Select(c => c.Code).Should().Contain(["UnpostedJournals", "UnsyncedOffline", "UnreconciledBank", "OpenStocktakes", "Grni", "ApprovalsInProgress"]);
        }

        long instanceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Two statement lines in the period, one after it (ignored).
            IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            (await banking.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 10), "In period 1", 100m, null),
                new StatementLineImport(new DateOnly(2026, 3, 20), "In period 2", -40m, null),
                new StatementLineImport(new DateOnly(2026, 4, 2), "Next period", 10m, null),
            ])).IsSuccess.Should().BeTrue();

            // Goods received (10 @ 2.50) but not invoiced → GRNI 25.00.
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "PC-PO1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 10m, 2.50m)],
            })).Value.PurchaseOrderId;
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = "PC-GRN1", Date = new DateOnly(2026, 3, 5), Lines = [new ReceiveLine(lineId, 10m)] })).IsSuccess.Should().BeTrue();

            // An approval still in progress.
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            instanceId = (await workflow.StartAsync(companyId, "PurchaseRequisition", "PR-1", 500m, initiatedByUserId: 1)).Value.InstanceId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            PeriodCloseChecklist list = await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>().ChecklistAsync(companyId, periodEnd);

            list.Outstanding.Should().Be(3);
            PeriodCloseCheck bank = list.Checks.Single(c => c.Code == "UnreconciledBank");
            bank.Count.Should().Be(2); // the April line is outside the period
            bank.Passes.Should().BeFalse();

            PeriodCloseCheck grni = list.Checks.Single(c => c.Code == "Grni");
            grni.Count.Should().Be(1);
            grni.Value.Should().Be(25.00m);

            list.Checks.Single(c => c.Code == "ApprovalsInProgress").Count.Should().Be(1);
            list.Checks.Single(c => c.Code == "UnpostedJournals").Count.Should().Be(0);

            // Only blocking checks stop the close; GRNI and approvals are warnings, unreconciled bank lines block.
            list.Checks.Single(c => c.Code == "UnreconciledBank").IsBlocking.Should().BeTrue();
            list.Checks.Single(c => c.Code == "Grni").IsBlocking.Should().BeFalse();
            list.IsReadyToClose.Should().BeFalse();

            // Approve the workflow: its check clears (the others remain).
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            (await workflow.ActAsync(instanceId, 7, WorkflowActionType.Approve, "ok")).Value.Should().Be(WorkflowStatus.Approved);
            PeriodCloseChecklist after = await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>().ChecklistAsync(companyId, periodEnd);
            after.Checks.Single(c => c.Code == "ApprovalsInProgress").Count.Should().Be(0);
            after.Outstanding.Should().Be(2);
        }
    }
}
