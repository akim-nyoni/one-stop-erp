using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget-holder alerts: stationery at 80% of its budget, travel at 92% through a commitment and salaries at 125%
/// raise 100, 90 and 75 alerts, most used first; depreciation at 10% raises none.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetUtilisationAlertTests
{
    private readonly AccountingFixture _fixture;

    public BudgetUtilisationAlertTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Votes_raise_the_highest_threshold_they_reach()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 3, 1_000m), new NewBudgetLine("6700", 3, 500m), new NewBudgetLine("6100", 3, 200m), new NewBudgetLine("6000", 3, 1_000m)])).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        foreach ((string account, decimal amount) in new[] { ("6800", 800m), ("6100", 250m), ("6000", 100m) })
        {
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                Lines =
                [
                    new JournalLineRequest { AccountCode = account, Side = JournalSide.Debit, TransactionAmount = amount },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
                ],
            })).IsSuccess.Should().BeTrue();
        }

        (await budgeting.CommitAsync(companyId, yearId, BudgetType.OriginalApproved, null, "6700", 3, 460m, "PO-TRAVEL")).IsSuccess.Should().BeTrue();

        IReadOnlyList<UtilisationAlert> alerts = await scope.ServiceProvider.GetRequiredService<IBudgetControlService>()
            .UtilisationAlertsAsync(companyId, yearId, 3, BudgetUtilisation.DefaultThresholds);
        alerts.Select(a => (a.AccountCode, a.PercentUsed, a.ThresholdCrossed)).Should().Equal(
            ("6100", 125m, 100m),
            ("6700", 92m, 90m),
            ("6800", 80m, 75m));
    }
}
