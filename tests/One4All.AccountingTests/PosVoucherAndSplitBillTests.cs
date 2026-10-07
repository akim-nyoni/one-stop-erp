using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers paying at the till from a gift card (part voucher, part cash; the card charged only when the sale posts; refused when the
/// card cannot cover it, is blocked, is in another currency or would give change; given back when the sale is voided) and splitting a
/// bill between payers (by items and evenly, each part its own sale, all or none).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosVoucherAndSplitBillTests
{
    private readonly AccountingFixture _fixture;

    public PosVoucherAndSplitBillTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vouchers_pay_at_the_till_and_bills_split()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "VS", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "VSTILL", "Till");
        Item mug = new(companyId, "VS-MUG", "Mug") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Item tea = new(companyId, "VS-TEA", "Tea") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "VS-WH", "Shop floor");
        db.AddRange(terminal, mug, tea, shop);
        await db.SaveChangesAsync();
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach (Item item in new[] { mug, tea })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 50m, UnitCost = 1m, CreditAccountCode = "1100" }))
                .IsSuccess.Should().BeTrue();
        }

        IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
        (await vouchers.IssueGiftCardAsync(new IssueGiftCardRequest
        {
            CompanyId = companyId, CardNumber = "VS-GC-1", Currency = Scenario.Usd, Value = 50m, IssueDate = new DateOnly(2026, 6, 1), ExpiryDate = new DateOnly(2027, 5, 31),
            CashAccountCode = "1100", LiabilityAccountCode = "2300",
        })).IsSuccess.Should().BeTrue();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 6, 2);
        PosSaleLineRequest Mugs(decimal quantity) => new(mug.Id, "Mug", quantity, 8m, 8m * quantity, 0m, "4000");
        PosTenderRequest Voucher(decimal amount, string card = "vs-gc-1") => new(TenderType.Voucher, Scenario.Usd, amount, "2300", IsChange: false, VoucherNumber: card);
        PosTenderRequest Cash(decimal amount, bool change = false) => new(TenderType.Cash, Scenario.Usd, amount, "1000", IsChange: change);
        CompleteSaleRequest Sale(string number, PosSaleLineRequest[] lines, params PosTenderRequest[] tenders) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, Lines = lines, Tenders = tenders,
        };
        async Task<decimal> Balance() => (await vouchers.GetBalanceAsync(companyId, "VS-GC-1")).Value.Balance;

        // Refusals leave the card untouched.
        (await pos.CompleteSaleAsync(Sale("VS-X1", [Mugs(10m)], Voucher(80m)))).Error.Code.Should().Be("Voucher.InsufficientBalance");
        (await pos.CompleteSaleAsync(Sale("VS-X2", [Mugs(1m)], Voucher(10m), Cash(2m, change: true)))).Error.Code.Should().Be("Pos.VoucherOverpays");
        (await pos.CompleteSaleAsync(Sale("VS-X3", [Mugs(1m)], Voucher(8m, "NOPE")))).Error.Code.Should().Be("Voucher.NotFound");
        (await pos.CompleteSaleAsync(Sale("VS-X4", [Mugs(1m)], new PosTenderRequest(TenderType.Voucher, Scenario.Usd, 8m, "2300", IsChange: false)))).Error.Code.Should().Be("Pos.VoucherTender");
        (await pos.CompleteSaleAsync(Sale("VS-X5", [Mugs(1m)], Voucher(8m), Cash(1m)))).Error.Code.Should().Be("Pos.Unbalanced");
        (await Balance()).Should().Be(50m);

        // 5 mugs (40): 30 from the card, the rest in cash with change.
        CompleteSaleResult paid = (await pos.CompleteSaleAsync(Sale("VS-1", [Mugs(5m)], Voucher(30m), Cash(20m), Cash(10m, change: true)))).Value;
        (await Balance()).Should().Be(20m);
        (await db.Set<SaleTender>().AsNoTracking().SingleAsync(t => t.SaleId == paid.SaleId && t.TenderType == TenderType.Voucher)).VoucherNumber.Should().Be("VS-GC-1");
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], day)).Value;
        tb.Rows.Single(r => r.AccountCode == "2300").Credit.Should().Be(20m);

        // Voiding the sale gives the 30 back to the card.
        (await pos.VoidSaleAsync(companyId, paid.SaleId, day, "Customer changed mind")).IsSuccess.Should().BeTrue();
        (await Balance()).Should().Be(50m);
        (await db.Set<GiftCardTransaction>().AsNoTracking().Where(t => t.CompanyId == companyId).Select(t => t.Event).ToListAsync())
            .Should().Contain([GiftCardEvent.Redeemed, GiftCardEvent.Reinstated]);

        // A blocked card cannot pay.
        (await scope.ServiceProvider.GetRequiredService<IGiftCardServicing>().BlockAsync(companyId, "VS-GC-1", day, "Lost")).IsSuccess.Should().BeTrue();
        (await pos.CompleteSaleAsync(Sale("VS-X6", [Mugs(1m)], Voucher(8m)))).Error.Code.Should().Be("Pos.VoucherNotUsable");

        // Split by items: one pays the tea and a mug by card, the other the remaining two mugs in cash.
        PosSaleLineRequest[] basket = [Mugs(3m), new PosSaleLineRequest(tea.Id, "Tea", 1m, 5m, 5m, 0m, "4000")];
        CompleteSaleRequest table = Sale("VS-T1", basket);
        var split = await pos.SplitBillAsync(new SplitBillRequest
        {
            Basket = table, Parts = [[new SplitShare(0, 1m), new SplitShare(1, 1m)], [new SplitShare(0, 2m)]],
            Tenders = [[new PosTenderRequest(TenderType.Card, Scenario.Usd, 13m, "1000", IsChange: false)], [Cash(16m)]],
        });
        split.IsSuccess.Should().BeTrue(split.IsFailure ? split.Error.ToString() : null);
        split.Value.Select(s => s.TotalGross).Should().Equal(13m, 16m);
        (await db.Set<Sale>().AsNoTracking().Where(s => s.CompanyId == companyId && s.SaleNumber.StartsWith("VS-T1-")).Select(s => s.SaleNumber).OrderBy(n => n).ToListAsync())
            .Should().Equal("VS-T1-1", "VS-T1-2");

        // A part that does not balance undoes the whole split.
        var refused = await pos.SplitBillAsync(new SplitBillRequest
        {
            Basket = Sale("VS-T2", basket), Parts = SplitBill.Evenly(basket, 2), Tenders = [[Cash(14.5m)], [Cash(10m)]],
        });
        refused.Error.Code.Should().Be("Pos.Unbalanced");
        refused.Error.Message.Should().StartWith("Part 2:");
        (await db.Set<Sale>().AsNoTracking().AnyAsync(s => s.CompanyId == companyId && s.SaleNumber.StartsWith("VS-T2-"))).Should().BeFalse();
        (await pos.SplitBillAsync(new SplitBillRequest { Basket = Sale("VS-T3", basket), Parts = SplitBill.Evenly(basket, 2), Tenders = [[Cash(14.5m)], [Cash(14.5m)]] }))
            .Value.Select(s => s.TotalGross).Should().Equal(14.5m, 14.5m);
        (await pos.SplitBillAsync(new SplitBillRequest { Basket = Sale("VS-T4", basket), Parts = SplitBill.Evenly(basket, 2), Tenders = [[Cash(14.5m)]] })).Error.Code.Should().Be("Pos.SplitTenders");
    }
}
