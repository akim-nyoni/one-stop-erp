using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the loyalty programme: members earn points (more in a higher tier), redeem them within their balance, lose unused points at
/// expiry oldest first, and the points' value is carried as a liability that issuing builds and expiry releases.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LoyaltyTests
{
    private readonly AccountingFixture _fixture;

    public LoyaltyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Members_earn_redeem_and_lose_points()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2496", "Loyalty Points Liability", AccountType.Liability), new LedgerAccount(companyId, "6496", "Loyalty Cost", AccountType.Expense));
        await db.SaveChangesAsync();

        ILoyaltyService loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();
        (await loyalty.EnrolAsync(companyId, "L-1", "Lee", null, new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await loyalty.EarnAsync(companyId, "L-1", 10m, new DateOnly(2026, 1, 2), "S-0")).Error.Code.Should().Be("Loyalty.NoScheme");
        (await loyalty.ConfigureAsync(new LoyaltySchemeRequest(companyId, 1m, 0.01m, 3, "2496", "6496", [("Silver", 200m, 2m)]))).IsSuccess.Should().BeTrue();
        (await loyalty.EnrolAsync(companyId, "l-2", "Lin", null, new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await loyalty.EnrolAsync(companyId, "L-2", "Lin again", null, new DateOnly(2026, 1, 1))).Error.Code.Should().Be("Loyalty.MemberExists");

        // 150 points in January, then Silver (200 in a year) doubles the next earning.
        (await loyalty.EarnAsync(companyId, "L-2", 150.75m, new DateOnly(2026, 1, 10), "S-1")).Value.Should().Be(new LoyaltyEarned(150m, null, 150m));
        (await loyalty.EarnAsync(companyId, "L-2", 60m, new DateOnly(2026, 1, 20), "S-2")).Value.Should().Be(new LoyaltyEarned(60m, null, 210m));
        (await loyalty.EarnAsync(companyId, "L-2", 40m, new DateOnly(2026, 2, 5), "S-3")).Value.Should().Be(new LoyaltyEarned(80m, "Silver", 290m));

        (await loyalty.RedeemAsync(companyId, "L-2", 500m, new DateOnly(2026, 2, 6), "S-4")).Error.Code.Should().Be("Loyalty.InsufficientPoints");
        (await loyalty.RedeemAsync(companyId, "L-2", 100m, new DateOnly(2026, 2, 6), "S-4")).Value.Should().Be(1m);

        // At 1 May the January points (210, 100 of them used) are past expiry: 110 expire; February's 80 remain.
        (await loyalty.ExpireAsync(companyId, new DateOnly(2026, 5, 1), "LOY-EXP-05")).Value.Should().Be((1, 110m, 1.10m));
        LoyaltyStatement statement = (await loyalty.StatementAsync(companyId, "L-2", new DateOnly(2026, 5, 1))).Value;
        statement.Balance.Should().Be(80m);
        statement.BalanceValue.Should().Be(0.80m);
        statement.Movements.Select(m => m.Kind).Should().Equal(LoyaltyMovementKind.Earn, LoyaltyMovementKind.Earn, LoyaltyMovementKind.Earn, LoyaltyMovementKind.Redeem, LoyaltyMovementKind.Expire);

        // Issued 2.90 of liability, released 1.10 at expiry; the 1.00 redeemed is cleared when it is tendered at the till.
        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 5, 31));
        tb.Rows.Where(r => r.AccountCode == "2496").Sum(r => r.Credit - r.Debit).Should().Be(1.80m);
        tb.Rows.Where(r => r.AccountCode == "6496").Sum(r => r.Debit - r.Credit).Should().Be(1.80m);
    }
}
