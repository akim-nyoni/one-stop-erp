using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>RC50 sets sundry collections per income vote against the in-force revenue budget for the periods in the range.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CollectionVsBudgetTests
{
    private readonly AccountingFixture _fixture;

    public CollectionVsBudgetTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Collections_are_set_against_the_revenue_budget_by_vote()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();

        // Budget: sundry income (4400) 1,000 a month in April and May, service charges (4600) 300 in May; a draft version that must be ignored.
        Budget revenue = new(companyId, yearId, "Revenue 2026", BudgetType.OriginalApproved, null, 1);
        revenue.AddLine("4400", 4, 1_000m);
        revenue.AddLine("4400", 5, 1_000m);
        revenue.AddLine("4600", 5, 300m);
        revenue.Approve();
        revenue.Activate();
        Budget draft = new(companyId, yearId, "Draft", BudgetType.DraftSubmission, null, 2);
        draft.AddLine("4400", 5, 9_999m);
        db.Budgets.AddRange(revenue, draft);
        CashierStation station = new(companyId, "TILL-RC50", "Counter", "R5");
        db.CashierStations.Add(station);
        await db.SaveChangesAsync();

        // Collected in May: 650 of sundry income, nothing for service charges.
        IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
        long session = (await rcp.OpenSessionAsync(companyId, station.Id, 7101, new DateOnly(2026, 5, 6), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
        var posted = await rcp.PostReceiptAsync(new One4All.Application.Abstractions.Receipting.ReceiptRequest
        {
            CompanyId = companyId, SessionId = session, Date = new DateOnly(2026, 5, 6), CashAccountCode = "1000",
            Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 650m, "4400", "Hall hire")], Tenders = [new ReceiptTenderRequest(TenderType.Cash, 650m)],
        });
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Message : null);

        IReportDataProvider reports = scope.ServiceProvider.GetServices<IReportDataProvider>().OfType<ReceiptingReportDataProvider>().Single();
        var rows = await reports.GetRowsAsync(companyId, "RC50", new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 5, 1), ["ToDate"] = new DateOnly(2026, 5, 31) });
        var sundry = rows.Single(r => ((string)r["Vote"]!).StartsWith("4400", StringComparison.Ordinal));
        (sundry["Budget"], sundry["Actual"], sundry["Variance"]).Should().Be((1_000m, 650m, -350m));
        var service = rows.Single(r => ((string)r["Vote"]!).StartsWith("4600", StringComparison.Ordinal));
        (service["Budget"], service["Actual"], service["Variance"]).Should().Be((300m, 0m, -300m));
    }
}
