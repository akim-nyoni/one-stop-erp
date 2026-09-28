using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the bulk gift-card breakage run: expired cards with a residual balance are recognised as income,
/// while a not-yet-expired card is left alone.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BreakageRunTests
{
    private const string Cash = "1000";
    private const string Liability = "2300"; // Gift Card Liability
    private const string Breakage = "4900";  // Breakage Income

    private readonly AccountingFixture _fixture;

    public BreakageRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Expired_cards_with_balances_are_swept_to_breakage_income()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();

            // Two cards expired by the run date, one still valid.
            await vouchers.IssueGiftCardAsync(Issue(companyId, "GC-EXP1", 50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28)));
            await vouchers.IssueGiftCardAsync(Issue(companyId, "GC-EXP2", 30m, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 15)));
            await vouchers.IssueGiftCardAsync(Issue(companyId, "GC-LIVE", 40m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

            // GC-EXP2 was partly redeemed (10 spent → 20 residual).
            await vouchers.RedeemAsync(companyId, "GC-EXP2", 10m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
            var run = await vouchers.RecogniseExpiredBreakageAsync(companyId, new DateOnly(2026, 4, 1), Liability, Breakage);

            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.CardsExpired.Should().Be(2);          // EXP1 (50) + EXP2 (20 residual)
            run.Value.TotalBreakage.Should().Be(70m);

            // The live card is untouched.
            (await vouchers.GetBalanceAsync(companyId, "GC-LIVE")).Value.Balance.Should().Be(40m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 1));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == Breakage && r.Credit == 70m); // breakage income recognised
        }
    }

    private static IssueGiftCardRequest Issue(long companyId, string number, decimal value, DateOnly issued, DateOnly expiry) => new()
    {
        CompanyId = companyId, CardNumber = number, Currency = Scenario.Usd, Value = value,
        IssueDate = issued, ExpiryDate = expiry, CashAccountCode = Cash, LiabilityAccountCode = Liability,
    };
}
