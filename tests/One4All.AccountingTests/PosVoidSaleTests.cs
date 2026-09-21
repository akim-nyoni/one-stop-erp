using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PosVoidSaleTests
{
    private readonly AccountingFixture _fixture;

    public PosVoidSaleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Voiding_a_sale_refunds_the_tender_unwinds_revenue_and_returns_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long terminalId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "HO", "Head Office");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            PosTerminal terminal = new(companyId, branch.Id, "TILL2", "Front Till");
            db.PosTerminals.Add(terminal);
            Item item = new(companyId, "PV-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "PVWH", "Shop Floor");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            terminalId = terminal.Id;
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 20), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });
        }

        long saleId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            long sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;
            var sale = await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 1,
                SaleNumber = "PV-S1", SaleDate = new DateOnly(2026, 4, 20), WarehouseId = warehouseId, TaxAccountCode = "2200",
                Lines = [new PosSaleLineRequest(itemId, "Soda", 10m, 2.00m, 20m, 3m, "4000")],
                Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 23m, "1000", IsChange: false)],
            });
            sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.ToString() : null);
            saleId = sale.Value.SaleId;
        }

        // After the sale: stock 90, revenue 20, VAT 3, cash 23, COGS 10.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "PV-SODA" && l.Quantity == 90m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            (await pos.VoidSaleAsync(companyId, saleId, new DateOnly(2026, 4, 21), "  ", "2200")).IsFailure.Should().BeTrue(); // reason required
            (await pos.VoidSaleAsync(companyId, saleId, new DateOnly(2026, 4, 21), "Customer changed mind", "2200")).IsSuccess.Should().BeTrue();
            (await pos.VoidSaleAsync(companyId, saleId, new DateOnly(2026, 4, 21), "again", "2200")).IsFailure.Should().BeTrue(); // not completed anymore
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "PV-SODA" && l.Quantity == 100m); // stock returned

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "4000" && (r.Debit != 0m || r.Credit != 0m)); // revenue nets to zero
            tb.Rows.Should().NotContain(r => r.AccountCode == "2200" && (r.Debit != 0m || r.Credit != 0m)); // VAT nets to zero
            tb.Rows.Should().NotContain(r => r.AccountCode == "5000" && (r.Debit != 0m || r.Credit != 0m)); // COGS nets to zero
        }
    }
}
