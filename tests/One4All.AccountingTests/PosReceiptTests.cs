using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers rendering a printable receipt for a stored POS sale.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PosReceiptTests
{
    private readonly AccountingFixture _fixture;

    public PosReceiptTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_completed_sale_renders_a_receipt()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long terminalId;
        long warehouseId;
        long itemId;
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

        long saleId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            long sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;

            var sale = await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 1,
                SaleNumber = "R-42", SaleDate = new DateOnly(2026, 3, 2), WarehouseId = warehouseId, TaxAccountCode = "2200",
                Lines = [new PosSaleLineRequest(itemId, "Soda 500ml", 10m, 2.00m, 20m, 3m, "4000")],
                Tenders =
                [
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 25m, "1000", IsChange: false),
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2m, "1000", IsChange: true),
                ],
            });
            saleId = sale.Value.SaleId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
            var result = await receipts.RenderReceiptTextAsync(companyId, saleId);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string text = result.Value;
            text.Should().Contain("Test Trading Co");     // company heading
            text.Should().Contain("Receipt: R-42");
            text.Should().Contain("Front Till");           // terminal name
            text.Should().Contain("Soda 500ml");
            text.Should().Contain("TOTAL");
            text.Should().Contain("23.00");                // gross
            text.Should().Contain("Cash");
            text.Should().Contain("Change");
            text.Should().Contain("2.00");                 // change given
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
            (await receipts.RenderReceiptTextAsync(companyId, 999_999)).Error.Code.Should().Be("Pos.SaleNotFound");
        }
    }
}
