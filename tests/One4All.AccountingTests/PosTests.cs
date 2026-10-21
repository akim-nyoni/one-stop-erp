using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PosTests
{
    private readonly AccountingFixture _fixture;

    public PosTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Complete_sale_posts_revenue_tax_cogs_and_reads_the_till()
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
            PosTerminal terminal = new(companyId, branch.Id, "TILL1", "Front Till");
            db.PosTerminals.Add(terminal);
            Item item = new(companyId, "SODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Shop Floor");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            terminalId = terminal.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        // Receive 100 @ 1.00 so there is stock to sell.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });
        }

        long sessionId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;

            // One open session per till: the same cashier opening it again carries on in it; another cashier is refused (sixteenth audit #5).
            (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value.Should().Be(sessionId);
            (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 2, openingFloat: 50m)).Error.Code.Should().Be("Pos.TillAlreadyOpen");

            // Sell 10 @ 2.00, net 20 + VAT 3 = 23; tender cash 25 with 2 change.
            var sale = await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 1,
                SaleNumber = "S-1", SaleDate = new DateOnly(2026, 3, 2), WarehouseId = warehouseId, TaxAccountCode = "2200",
                Lines = [new PosSaleLineRequest(itemId, "Soda 500ml", 10m, 2.00m, 20m, 3m, "4000")],
                Tenders =
                [
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 25m, "1000", IsChange: false),
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2m, "1000", IsChange: true),
                ],
            });
            sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.ToString() : null);
            sale.Value.TotalGross.Should().Be(23m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();

            var xread = await pos.XReadAsync(sessionId);
            xread.Value.SaleCount.Should().Be(1);
            xread.Value.TotalGross.Should().Be(23m);
            xread.Value.TenderTotals[TenderType.Cash].Should().Be(25m); // gross cash in (change excluded)
            xread.Value.ZReadNumber.Should().BeNull();

            var zread = await pos.CloseTillAsync(sessionId);
            zread.Value.ZReadNumber.Should().Be(1);

            // A cashed-up session takes no more sales.
            (await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 1,
                SaleNumber = "S-LATE", SaleDate = new DateOnly(2026, 3, 2), WarehouseId = warehouseId, TaxAccountCode = "2200",
                Lines = [new PosSaleLineRequest(itemId, "Soda 500ml", 1m, 2.00m, 2m, 0.3m, "4000")],
                Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2.3m, "1000", IsChange: false)],
            })).Error.Code.Should().Be("Pos.SessionClosed");
        }

        // Stock reduced by the sale (100 received − 10 sold).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "SODA" && l.Quantity == 90m && l.Value == 90m);
        }
    }
}
