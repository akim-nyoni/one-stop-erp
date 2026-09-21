using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventoryReversalTests
{
    private readonly AccountingFixture _fixture;

    public InventoryReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reversing_an_issue_returns_stock_at_the_original_cost_and_inverts_the_journal()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long itemId;
        long warehouseId;
        long issueMovementId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item item = new(companyId, "IR-WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "IRWH", "Store");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 16), Quantity = 100m, UnitCost = 5.00m, CreditAccountCode = "1100" });
            var issue = await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 16), Quantity = 30m, Reference = "SALE-1" });
            issue.IsSuccess.Should().BeTrue(issue.IsFailure ? issue.Error.ToString() : null);
            issue.Value.Value.Should().Be(150m); // 30 × 5
            issueMovementId = issue.Value.MovementId;
        }

        // After the issue: 70 on hand, COGS 150.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "IR-WIDGET" && l.Quantity == 70m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 150m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var reversal = await inv.ReverseIssueAsync(companyId, issueMovementId, new DateOnly(2026, 4, 17), "REV-SALE-1");
            reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);
            reversal.Value.Value.Should().Be(150m);

            // Reversing the same movement again is refused.
            (await inv.ReverseIssueAsync(companyId, issueMovementId, new DateOnly(2026, 4, 17))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Stock is back to 100.
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "IR-WIDGET" && l.Quantity == 100m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // COGS nets to zero (150 issued, 150 reversed).
            tb.Rows.Should().NotContain(r => r.AccountCode == "5000" && (r.Debit != 0m || r.Credit != 0m));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // A receipt cannot be reversed via ReverseIssueAsync.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            StockMovement receipt = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Set<StockMovement>(), m => m.Type == StockMovementType.Receipt && m.CompanyId == companyId);
            (await inv.ReverseIssueAsync(companyId, receipt.Id, new DateOnly(2026, 4, 17))).IsFailure.Should().BeTrue();
        }
    }
}
