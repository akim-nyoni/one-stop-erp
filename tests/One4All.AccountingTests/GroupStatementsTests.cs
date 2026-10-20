using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A parent pays 800 for 80% of a subsidiary issuing 1,000 of shares; the subsidiary sells 300 to the parent on account. The group statements
/// eliminate the trading and the balances, set the investment against 80% of the capital (no goodwill), give the outside shareholders 260 of net
/// assets and 60 of the subsidiary's profit, and balance.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class GroupStatementsTests
{
    private readonly AccountingFixture _fixture;

    public GroupStatementsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Group_statements_eliminate_intercompany_and_show_the_non_controlling_interest()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long parent = await Scenario.SeedAsync(db);
        long subsidiary = await Scenario.SeedAsync(db);
        foreach (long company in new[] { parent, subsidiary })
        {
            db.Accounts.AddRange(new LedgerAccount(company, "1810", "Investment in Subsidiary", AccountType.Asset), new LedgerAccount(company, "3000", "Share Capital", AccountType.Equity),
                new LedgerAccount(company, "4050", "Intercompany Sales", AccountType.Income), new LedgerAccount(company, "6050", "Intercompany Purchases", AccountType.Expense),
                new LedgerAccount(company, "1700", "Due from Group", AccountType.Asset), new LedgerAccount(company, "2700", "Due to Group", AccountType.Liability));
        }

        await db.SaveChangesAsync();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task Post(long company, string debit, string credit, decimal amount) => (await engine.PostAsync(new JournalRequest
        {
            CompanyId = company, PostingDate = new DateOnly(2026, 3, 1), TransactionCurrency = Scenario.Usd, Narrative = "Group",
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        })).IsSuccess.Should().BeTrue();
        await Post(parent, "1810", "1100", 800m);
        await Post(subsidiary, "1100", "3000", 1000m);
        await Post(subsidiary, "1700", "4050", 300m);
        await Post(parent, "6050", "2700", 300m);

        GroupStatements g = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().GroupStatementsAsync(new GroupStatementsRequest
        {
            CompanyIds = [parent, subsidiary], ParentCompanyId = parent, AsAtDate = new DateOnly(2026, 3, 31), IntercompanyBalanceAccounts = ["1700", "2700"],
            IntercompanyRevenueAccounts = ["4050"], IntercompanyExpenseAccounts = ["6050"], InvestmentAccountCode = "1810", SubsidiaryShareCapitalAccounts = ["3000"],
            Ownership = new Dictionary<long, decimal> { [subsidiary] = 80m },
        })).Value;

        (g.IntercompanyTradingEliminated, g.IntercompanyBalancesEliminated, g.Goodwill).Should().Be((300m, 300m, 0m));
        (g.Profit, g.NciShareOfProfit, g.ProfitToOwners).Should().Be((0m, 60m, -60m));
        g.Assets.Select(l => (l.AccountCode, l.Amount)).Should().Equal(("1100", 200m));
        (g.NonControllingInterest, g.OwnersEquity).Should().Be((260m, -60m));
        g.Balances.Should().BeTrue();
    }
}
