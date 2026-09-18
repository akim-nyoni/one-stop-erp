using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class BudgetingTests
{
    private readonly AccountingFixture _fixture;

    public BudgetingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budget_vs_actual_joins_gl_actuals_and_checks_availability()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        }

        // Post an actual expense of 500 to Salaries (6100) in January.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            var post = await posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 15),
                TransactionCurrency = Scenario.Usd, DocumentType = "JNL", DocumentNumber = "ACT-1",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 500m },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 500m },
                ],
            });
            post.IsSuccess.Should().BeTrue(post.IsFailure ? post.Error.ToString() : null);
        }

        long budgetId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            var created = await budgeting.CreateBudgetAsync(companyId, fiscalYearId, "2026 Original", BudgetType.OriginalApproved, "ADMIN",
                [new NewBudgetLine("6100", 1, 1000m)]);
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
            budgetId = created.Value;

            var activate = await budgeting.ActivateAsync(companyId, budgetId);
            activate.IsSuccess.Should().BeTrue(activate.IsFailure ? activate.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            var bva = await budgeting.BudgetVsActualAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", asAtPeriod: 1);
            bva.IsSuccess.Should().BeTrue(bva.IsFailure ? bva.Error.ToString() : null);
            var row = bva.Value.Rows.Single(r => r.AccountCode == "6100");
            row.Budget.Should().Be(1000m);
            row.Actual.Should().Be(500m);
            row.Variance.Should().Be(500m);
            row.PercentUsed.Should().Be(50m);

            var within = await budgeting.CheckAvailabilityAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6100", 400m, 1);
            within.Value.Available.Should().Be(500m);
            within.Value.Passes.Should().BeTrue();

            var over = await budgeting.CheckAvailabilityAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6100", 600m, 1);
            over.Value.Passes.Should().BeFalse();
        }

        // Budgets never touch the trial balance.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Application.Abstractions.Reporting.IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Reporting.IGeneralLedgerReports>();
            var tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6100" && r.Debit == 500m); // only the posted actual, not the 1000 budget
        }
    }
}
