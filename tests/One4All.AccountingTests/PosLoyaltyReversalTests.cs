using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Audit #19: a member's points are earned inside the sale's own unit of work (a refused sale earns nothing); a void takes back the points the
/// sale earned and gives back those it redeemed; a partial refund does both in proportion to what it pays back, and refunding the rest brings the
/// member back exactly where they started, so buying, earning and refunding farms no points.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosLoyaltyReversalTests
{
    // A cashier id no user created by the test can be given, so the supervisor is never the cashier by accident.
    private const long Cashier = 900_019;

    private readonly AccountingFixture _fixture;

    public PosLoyaltyReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Points_are_earned_with_the_sale_and_undone_by_its_void_or_refund()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2496", "Loyalty Points Liability", AccountType.Liability), new LedgerAccount(companyId, "6496", "Loyalty Cost", AccountType.Expense));
        Branch branch = new(companyId, "LR", "Loyalty Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal till = new(companyId, branch.Id, "LRTILL", "Loyalty Till");
        Item soda = new(companyId, "LR-SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "LR-WH", "Shop");
        db.AddRange(till, soda, shop);
        await db.SaveChangesAsync();

        DateOnly day = new(2026, 6, 2);
        ILoyaltyService loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        (await loyalty.ConfigureAsync(new LoyaltySchemeRequest(companyId, 1m, 0.01m, null, "2496", "6496", []))).IsSuccess.Should().BeTrue();
        (await loyalty.EnrolAsync(companyId, "LR-M1", "Member", null, day)).IsSuccess.Should().BeTrue();
        (await loyalty.EarnAsync(companyId, "LR-M1", 1000m, day, "SEED")).Value.Balance.Should().Be(1000m);
        async Task<decimal> Balance() => (await loyalty.StatementAsync(companyId, "LR-M1", day)).Value.Balance;

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, till.Id, Cashier, 0m)).Value;
        PosTenderRequest Cash(decimal amount) => new(TenderType.Cash, Scenario.Usd, amount, "1000", IsChange: false);
        PosTenderRequest Points(decimal amount) => new(TenderType.Loyalty, Scenario.Usd, amount, string.Empty, IsChange: false, VoucherNumber: "LR-M1");
        CompleteSaleRequest Sale(string number, decimal quantity, string? member, params PosTenderRequest[] tenders) => new()
        {
            CompanyId = companyId, TerminalId = till.Id, TillSessionId = session, CashierUserId = Cashier, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id,
            Lines = [new PosSaleLineRequest(soda.Id, "Soda", quantity, 10m, quantity * 10m, 0m, "4000")], Tenders = tenders, LoyaltyMemberNumber = member,
        };

        // Earned inside the sale: 20 spent earns 20 points; a sale naming a member the scheme does not know is refused whole.
        var earned = await pos.CompleteSaleAsync(Sale("LR-1", 2m, "lr-m1", Cash(20m)));
        earned.IsSuccess.Should().BeTrue(earned.IsFailure ? earned.Error.ToString() : null);
        earned.Value.Loyalty!.Value.Points.Should().Be(20m);
        (await Balance()).Should().Be(1020m);
        (await pos.CompleteSaleAsync(Sale("LR-X", 1m, "NOBODY", Cash(10m)))).Error.Code.Should().Be("Loyalty.MemberNotFound");
        (await db.Sales.AnyAsync(s => s.CompanyId == companyId && s.SaleNumber == "LR-X")).Should().BeFalse("the sale and its points stand or fall together");

        // A void gives back the 500 points redeemed and takes back the 10 earned.
        long voidable = (await pos.CompleteSaleAsync(Sale("LR-2", 1m, "LR-M1", Points(5m), Cash(5m)))).Value.SaleId;
        (await Balance()).Should().Be(1020m - 500m + 10m);
        (await pos.VoidSaleAsync(companyId, voidable, day, "Rung up in error")).IsSuccess.Should().BeTrue();
        (await Balance()).Should().Be(1020m, "the void undoes both the redemption and the earning");
        List<LoyaltyMovement> undone = await db.LoyaltyMovements.AsNoTracking()
            .Where(m => m.CompanyId == companyId && (m.Kind == LoyaltyMovementKind.EarnReversed || m.Kind == LoyaltyMovementKind.RedeemReversed)).ToListAsync();
        undone.Select(m => (m.Kind, m.Points)).Should().BeEquivalentTo([(LoyaltyMovementKind.EarnReversed, -10m), (LoyaltyMovementKind.RedeemReversed, 500m)]);

        // A sale of 40 paid 10 in points (1,000) and 30 cash earns 40; refunding a quarter of it takes back 10 and gives back 250, and refunding the
        // rest brings the member back to where they were before the sale.
        long refundable = (await pos.CompleteSaleAsync(Sale("LR-3", 4m, "LR-M1", Points(10m), Cash(30m)))).Value.SaleId;
        (await Balance()).Should().Be(1020m - 1000m + 40m);
        long line = await db.Set<SaleLine>().Where(l => l.SaleId == refundable).Select(l => l.Id).SingleAsync();
        long supervisor = await Scenario.UserWithAsync(db, "lr-supervisor", "Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel1);
        IPosEventService events = scope.ServiceProvider.GetRequiredService<IPosEventService>();
        PosRefundRequest Refund(decimal quantity) => new()
        {
            CompanyId = companyId, SaleId = refundable, TerminalId = till.Id, CashierUserId = Cashier, AuthorisedByUserId = supervisor, Date = day, RefundAccountCode = "1000",
            Reason = "Returned", Lines = [new PosRefundLine(line, quantity)], TillSessionId = session,
        };

        var quarter = await events.RefundAsync(Refund(1m));
        quarter.IsSuccess.Should().BeTrue(quarter.IsFailure ? quarter.Error.ToString() : null);
        (await Balance()).Should().Be(60m - 10m + 250m, "a quarter of the 40 earned is taken back and a quarter of the 1,000 redeemed given back");
        (await events.RefundAsync(Refund(3m))).IsSuccess.Should().BeTrue();
        (await Balance()).Should().Be(1020m, "the whole sale refunded leaves no points earned on it and every point it redeemed given back");

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 6, 30));
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("2496").Should().Be(-10.20m, "what stands: the 10.00 seeded and the 0.20 earned on LR-1; LR-2 and LR-3 earned and redeemed nothing in the end");
        Net("6496").Should().Be(10.20m);
    }
}
