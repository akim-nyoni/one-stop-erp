using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the IAS 29 restated trial balance with the CPI at 100 in January, 110 in February and 121 in March: capital of
/// 5,000 and equipment of 1,000 from January restate to 6,050 and 1,210; February sales of 1,100 to 1,210; March expenses
/// of 242 stay; the bank (monetary) stays at 4,858; the restated balances leave a monetary loss of 950. Without an index
/// for the reporting month the restatement is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HyperinflationAccountingTests
{
    private readonly AccountingFixture _fixture;

    public HyperinflationAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_trial_balance_is_restated_with_the_monetary_loss_balancing_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1490", "Equipment", AccountType.Asset) { Ias29 = Ias29Classification.NonMonetary });
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        await Post(engine, companyId, new DateOnly(2026, 1, 5), "1100", "3200", 5_000m);
        await Post(engine, companyId, new DateOnly(2026, 1, 15), "1490", "1100", 1_000m);
        await Post(engine, companyId, new DateOnly(2026, 2, 10), "1100", "4000", 1_100m);
        await Post(engine, companyId, new DateOnly(2026, 3, 5), "6800", "1100", 242m);

        IHyperinflationService ias29 = scope.ServiceProvider.GetRequiredService<IHyperinflationService>();
        (await ias29.SetIndexAsync(companyId, "cpi", new DateOnly(2026, 1, 31), 100m)).IsSuccess.Should().BeTrue();
        (await ias29.SetIndexAsync(companyId, "CPI", new DateOnly(2026, 2, 1), 110m)).IsSuccess.Should().BeTrue();
        (await ias29.SetIndexAsync(companyId, "CPI", new DateOnly(2026, 3, 1), 120m)).IsSuccess.Should().BeTrue();
        (await ias29.SetIndexAsync(companyId, "CPI", new DateOnly(2026, 3, 15), 121m)).IsSuccess.Should().BeTrue(); // corrects March
        (await ias29.SetIndexAsync(companyId, "CPI", new DateOnly(2026, 4, 1), 0m)).Error.Code.Should().Be("Ias29.InvalidIndex");

        RestatedTrialBalance tb = (await ias29.RestatedTrialBalanceAsync(companyId, new DateOnly(2026, 3, 31))).Value;
        tb.ClosingIndex.Should().Be(121m);
        tb.Rows.Select(r => (r.AccountCode, r.Monetary, r.Historical, r.Restated)).Should().Equal(
            ("1100", true, 4_858m, 4_858m),
            ("1490", false, 1_000m, 1_210m),
            ("3200", false, -5_000m, -6_050m),
            ("4000", false, -1_100m, -1_210m),
            ("6800", false, 242m, 242m));
        tb.MonetaryGainLoss.Should().Be(-950m);

        (await ias29.RestatedTrialBalanceAsync(companyId, new DateOnly(2026, 4, 30))).Error.Code.Should().Be("Ias29.MissingIndex");
    }

    private static async Task Post(IPostingEngine engine, long companyId, DateOnly date, string debit, string credit, decimal amount)
        => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
            Lines =
            [
                new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        })).IsSuccess.Should().BeTrue();
}
