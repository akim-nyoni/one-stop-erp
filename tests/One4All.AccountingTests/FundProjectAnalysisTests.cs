using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers project, fund and branch analysis: lines carry them (checked against the masters, required where the account says so),
/// reversals keep them, fund accounting makes every line name a fund (the company's default fills sub-ledger postings) and each fund
/// balance on its own, and budgets compare to actuals by fund and project.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FundProjectAnalysisTests
{
    private readonly AccountingFixture _fixture;

    public FundProjectAnalysisTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lines_carry_projects_funds_and_branches_and_funds_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Branches.Add(new Branch(companyId, "MUT", "Mutare"));
        await db.SaveChangesAsync();

        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        (await master.SaveFundAsync(companyId, "gen", "General rate fund", isRestricted: false)).IsSuccess.Should().BeTrue();
        (await master.SaveFundAsync(companyId, "WATER", "Water account", isRestricted: true)).IsSuccess.Should().BeTrue();
        (await master.SaveProjectAsync(companyId, "P-DAM", "Dam rehabilitation", "NOPE")).Error.Code.Should().Be("MasterData.FundNotFound");
        (await master.SaveProjectAsync(companyId, "P-DAM", "Dam rehabilitation", "water")).IsSuccess.Should().BeTrue();
        (await master.SaveProjectAsync(companyId, "P-OLD", "Finished works", null, isActive: false)).IsSuccess.Should().BeTrue();

        IPostingEngine gl = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        DateOnly day = new(2026, 3, 10);
        JournalRequest Journal(params JournalLineRequest[] lines) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = day, TransactionCurrency = Scenario.Usd, Narrative = "Analysis test", Lines = lines,
        };
        JournalLineRequest Line(string account, JournalSide side, decimal amount, string? project = null, string? fund = null, string? branch = null) => new()
        {
            AccountCode = account, Side = side, TransactionAmount = amount, Project = project, Fund = fund, Branch = branch,
        };

        PostingReceipt posted = (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 500m, "p-dam", "water", "mut"), Line("2410", JournalSide.Credit, 500m, fund: "WATER")))).Value;
        JournalLine expense = await db.Set<JournalLine>().AsNoTracking().SingleAsync(l => l.JournalEntryId == posted.JournalId && l.AccountCode == "6100");
        (expense.Project, expense.Fund, expense.Branch).Should().Be(("P-DAM", "WATER", "MUT"));

        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 1m, "P-NONE"), Line("2410", JournalSide.Credit, 1m)))).Error.Code.Should().Be("Posting.ProjectUnknown");
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 1m, "P-OLD"), Line("2410", JournalSide.Credit, 1m)))).Error.Code.Should().Be("Posting.ProjectUnknown", "a closed project takes no postings");
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 1m, fund: "X"), Line("2410", JournalSide.Credit, 1m)))).Error.Code.Should().Be("Posting.FundUnknown");
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 1m, branch: "HRE"), Line("2410", JournalSide.Credit, 1m)))).Error.Code.Should().Be("Posting.BranchUnknown");

        LedgerAccount capital = await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6100");
        capital.RequiresProject = true;
        await db.SaveChangesAsync();
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 1m), Line("2410", JournalSide.Credit, 1m)))).Error.Code.Should().Be("Posting.ProjectRequired");
        capital.RequiresProject = false;
        await db.SaveChangesAsync();

        // The reversal keeps the analysis.
        PostingReceipt reversed = (await gl.ReverseAsync(companyId, posted.JournalId, day, "Wrong vote")).Value;
        (await db.Set<JournalLine>().AsNoTracking().SingleAsync(l => l.JournalEntryId == reversed.JournalId && l.AccountCode == "6100")).Should()
            .Match<JournalLine>(l => l.Project == "P-DAM" && l.Fund == "WATER" && l.Branch == "MUT" && l.BaseCredit == 500m);

        // Fund accounting: each fund balances; the default fund fills lines without one.
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.FundAccounting = true;
        await db.SaveChangesAsync();
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 10m), Line("2410", JournalSide.Credit, 10m)))).Error.Code.Should().Be("Posting.FundRequired");
        company.DefaultFund = "GEN";
        await db.SaveChangesAsync();
        PostingReceipt defaulted = (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 10m), Line("2410", JournalSide.Credit, 10m)))).Value;
        (await db.Set<JournalLine>().AsNoTracking().Where(l => l.JournalEntryId == defaulted.JournalId).Select(l => l.Fund).ToListAsync()).Should().AllBe("GEN");
        (await gl.PostAsync(Journal(Line("6100", JournalSide.Debit, 75m, fund: "WATER"), Line("2410", JournalSide.Credit, 75m, fund: "GEN")))).Error.Code.Should().Be("Posting.FundOutOfBalance");
        (await gl.PostAsync(Journal(
            Line("6100", JournalSide.Debit, 75m, "P-DAM", "WATER"), Line("2410", JournalSide.Credit, 75m, fund: "WATER"),
            Line("6100", JournalSide.Debit, 40m, fund: "GEN"), Line("2410", JournalSide.Credit, 40m, fund: "GEN")))).IsSuccess.Should().BeTrue();

        // Budgets by fund and project compare to those actuals.
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "Funds 2026", BudgetType.OriginalApproved, null,
        [
            new NewBudgetLine("6100", 3, 200m, Project: "P-DAM", Fund: "water"), new NewBudgetLine("6100", 3, 100m, Fund: "GEN"),
        ])).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        IReadOnlyList<DimensionBudgetRow> byFund = (await budgeting.BudgetVsActualByDimensionAsync(companyId, yearId, BudgetType.OriginalApproved, null, BudgetDimension.Fund, null, 3)).Value;
        byFund.Should().ContainSingle(r => r.Value == "WATER" && r.AccountCode == "6100").Which.Should().Match<DimensionBudgetRow>(r => r.Budget == 200m && r.Actual == 75m && r.Remaining == 125m);
        byFund.Should().ContainSingle(r => r.Value == "GEN" && r.AccountCode == "6100").Which.Actual.Should().Be(50m);
        (await budgeting.BudgetVsActualByDimensionAsync(companyId, yearId, BudgetType.OriginalApproved, null, BudgetDimension.Project, "p-dam", 3)).Value
            .Should().ContainSingle(r => r.AccountCode == "6100").Which.Should().Match<DimensionBudgetRow>(r => r.Budget == 200m && r.Actual == 75m);
    }
}
