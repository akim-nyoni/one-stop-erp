using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Receipt layouts: the company's 80mm layout, a till's own 58mm one, the preview, and a sale's receipt printed in its till's layout.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ReceiptLayoutTests
{
    private readonly AccountingFixture _fixture;

    public ReceiptLayoutTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipts_print_in_their_tills_layout()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "RLB", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal wide = new(companyId, branch.Id, "RL-80", "Front till");
        PosTerminal narrow = new(companyId, branch.Id, "RL-58", "Kiosk");
        Item item = new(companyId, "RL-ITEM", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "RL-WH", "Shop floor");
        db.AddRange(wide, narrow, item, shop);
        await db.SaveChangesAsync();

        IReceiptLayoutService layouts = scope.ServiceProvider.GetRequiredService<IReceiptLayoutService>();
        (await layouts.SaveAsync(companyId, new ReceiptLayoutInput(null, 80, "TAX INVOICE", "12 Main Street | Tel 555 0101", "Goods are not returnable|Come again", ShowUnitPrice: true)))
            .IsSuccess.Should().BeTrue();
        (await layouts.SaveAsync(companyId, new ReceiptLayoutInput("rl-58", 58, Footer: "Thanks"))).IsSuccess.Should().BeTrue();
        (await layouts.SaveAsync(companyId, new ReceiptLayoutInput(null, 72))).Error.Code.Should().Be("Pos.ReceiptLayout");
        (await layouts.SaveAsync(companyId, new ReceiptLayoutInput("NOPE"))).Error.Code.Should().Be("Pos.TerminalNotFound");
        string preview = (await layouts.PreviewAsync(companyId, null)).Value;
        preview.Should().StartWith("80mm paper, 48 characters").And.Contain("12 Main Street").And.Contain("TAX INVOICE").And.Contain("Come again");

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        IPosReceiptService receipts = scope.ServiceProvider.GetRequiredService<IPosReceiptService>();
        async Task<string> SellOn(PosTerminal terminal, string number)
        {
            long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
            long sale = (await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 3, 2), WarehouseId = shop.Id,
                TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(item.Id, "Widget", 2m, 5m, 10m, 0m, "4000")], Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10m, "1000", false)],
            })).Value.SaleId;
            return (await receipts.RenderReceiptTextAsync(companyId, sale)).Value;
        }

        string front = await SellOn(wide, "RL-1");
        front.Split('\n').Select(l => l.TrimEnd('\r')).Should().OnlyContain(l => l.Length <= 48).And.Contain(l => l.Length == 48);
        front.Should().Contain("TAX INVOICE").And.Contain("  2 x 5.00").And.Contain("Goods are not returnable");
        string kiosk = await SellOn(narrow, "RL-2");
        kiosk.Split('\n').Select(l => l.TrimEnd('\r')).Should().OnlyContain(l => l.Length <= 32);
        kiosk.Should().Contain("Thanks").And.NotContain("Come again");
    }
}
