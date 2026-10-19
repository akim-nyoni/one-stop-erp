using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
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
/// A member with 1,000 points (worth 10.00) pays for an 8.00 mug with 5.00 of points and a 3.00 cheque: the points come off the balance, their value is
/// paid out of the points liability, the cheque goes to its account; spending more points than the member has, or points without a member, is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosPointsAndChequeTenderTests
{
    private readonly AccountingFixture _fixture;

    public PosPointsAndChequeTenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Points_and_cheques_pay_at_the_till()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2496", "Loyalty Points Liability", AccountType.Liability), new LedgerAccount(companyId, "6496", "Loyalty Cost", AccountType.Expense));
        Branch branch = new(companyId, "PT", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "PTTILL", "Till");
        Item mug = new(companyId, "PT-MUG", "Mug") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "PT-WH", "Shop floor");
        db.AddRange(terminal, mug, shop);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = mug.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 10m, UnitCost = 1m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ILoyaltyService loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        (await loyalty.ConfigureAsync(new LoyaltySchemeRequest(companyId, 1m, 0.01m, null, "2496", "6496", []))).IsSuccess.Should().BeTrue();
        (await loyalty.EnrolAsync(companyId, "PT-M1", "Member", null, new DateOnly(2026, 6, 1))).IsSuccess.Should().BeTrue();
        (await loyalty.EarnAsync(companyId, "PT-M1", 1000m, new DateOnly(2026, 6, 1), "EARN")).Value.Balance.Should().Be(1000m);

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        PosSaleLineRequest Mug = new(mug.Id, "Mug", 1m, 8m, 8m, 0m, "4000");
        PosTenderRequest Points(decimal amount, string? member = "pt-m1") => new(TenderType.Loyalty, Scenario.Usd, amount, string.Empty, IsChange: false, VoucherNumber: member);
        PosTenderRequest Cheque(decimal amount) => new(TenderType.Cheque, Scenario.Usd, amount, "1100", IsChange: false, VoucherNumber: "000123");
        CompleteSaleRequest Sale(string number, params PosTenderRequest[] tenders) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = new DateOnly(2026, 6, 2), WarehouseId = shop.Id,
            Lines = [Mug], Tenders = tenders,
        };

        (await pos.CompleteSaleAsync(Sale("PT-X1", Points(5m, null), Cheque(3m)))).Error.Code.Should().Be("Pos.LoyaltyTender");
        (await pos.CompleteSaleAsync(Sale("PT-X2", Points(8m, "NOBODY")))).IsFailure.Should().BeTrue();
        var sold = await pos.CompleteSaleAsync(Sale("PT-1", Points(5m), Cheque(3m)));
        sold.IsSuccess.Should().BeTrue(sold.IsFailure ? sold.Error.ToString() : null);
        (await loyalty.StatementAsync(companyId, "PT-M1", new DateOnly(2026, 6, 2))).Value.Balance.Should().Be(500m);
        (await pos.CompleteSaleAsync(Sale("PT-X3", Points(8m)))).Error.Code.Should().Be("Loyalty.InsufficientPoints");

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 6, 30));
        tb.IsBalanced.Should().BeTrue();
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        Net("2496").Should().Be(-10m + 5m, "10.00 of points issued, 5.00 spent at the till");
        (await db.Set<SaleTender>().AsNoTracking().Where(t => t.TenderType == TenderType.Loyalty && t.AccountCode == "2496").Select(t => t.VoucherNumber).ToListAsync())
            .Should().Contain("PT-M1");
    }
}
