using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #10: under fund accounting a vote is an account in a fund - its appropriation, availability, commitments and vote book row are the fund's own.</summary>
[Collection(AccountingCollection.Name)]
public sealed class FundVoteControlTests
{
    private readonly AccountingFixture _fixture;

    public FundVoteControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task One_funds_vote_never_pays_for_anothers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Funds.AddRange(new Fund(companyId, "GEN", "General fund", isRestricted: false), new Fund(companyId, "WAT", "Water fund", isRestricted: true));
        var company = await db.Companies.SingleAsync(c => c.Id == companyId);
        (company.FundControlEnabled, company.FundAccounting, company.DefaultFund) = (true, true, "GEN");
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6800")).BudgetEnabled = true;
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        // 6800 is appropriated 1,000 in the water fund only.
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgets.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null, [new NewBudgetLine("6800", 3, 1_000m, Fund: "WAT")])).Value;
        (await budgets.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        (await engine.PostAsync(Spend(companyId, "GEN", 40m))).Error.Code.Should().Be("Posting.NoAppropriation", "the general fund has no vote on 6800");
        var water300 = await engine.PostAsync(Spend(companyId, "WAT", 300m));
        water300.IsSuccess.Should().BeTrue(water300.IsFailure ? water300.Error.ToString() : null);

        // The gate: a demand with no fund is from the default (general) fund, which has nothing; the water fund has 700 left, less a 200 commitment.
        IBudgetGateService gate = scope.ServiceProvider.GetRequiredService<IBudgetGateService>();
        BudgetGateLine general = (await gate.CheckAsync(companyId, new DateOnly(2026, 3, 20), [new BudgetDemand("6800", null, 50m)])).Value.Lines.Single();
        (general.Fund, general.Available).Should().Be(("GEN", 0m));
        (await gate.CommitAsync(companyId, "FV-PO1", new DateOnly(2026, 3, 20), [new BudgetDemand("6800", null, 200m, "wat")])).Value.Should().Be(1);
        (await db.BudgetCommitments.AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.Reference == "FV-PO1")).Fund.Should().Be("WAT");
        BudgetGateLine water = (await gate.CheckAsync(companyId, new DateOnly(2026, 3, 20), [new BudgetDemand("6800", null, 600m, "WAT")])).Value.Lines.Single();
        (water.Fund, water.Available, water.Shortfall).Should().Be(("WAT", 500m, 100m));

        // The vote book by fund: the water vote carries its own spend and commitment.
        IReadOnlyList<VoteBookRow> book = await scope.ServiceProvider.GetRequiredService<IBudgetControlService>().VoteBookAsync(companyId, yearId, 12, byFund: true);
        VoteBookRow vote = book.Single(r => r.AccountCode == "6800");
        (vote.Fund, vote.Original, vote.Expenditure, vote.Commitments).Should().Be(("WAT", 1_000m, 300m, 200m));
    }

    private static JournalRequest Spend(long companyId, string fund, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
        Lines =
        [
            new JournalLineRequest { AccountCode = "6800", Fund = fund, Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "1100", Fund = fund, Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
