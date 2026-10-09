using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget methods: next year's draft built from this year's actuals with an uplift, a reforecast whose first months are the
/// actuals and the rest the budget, and the cash budget with sales collected over two months.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetMethodTests
{
    private readonly AccountingFixture _fixture;

    public BudgetMethodTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budgets_are_built_from_history_reforecast_and_turned_into_cash()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "BM-C", "Budget Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "BM-1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        long thisYear = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        long nextYear = (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateFiscalYearAsync(companyId, "2027", new DateOnly(2027, 1, 1))).Value;
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        IBudgetMethodService methods = scope.ServiceProvider.GetRequiredService<IBudgetMethodService>();

        // Next year's draft from this year's actuals plus 10%.
        long draft = (await budgeting.CreateBudgetAsync(companyId, nextYear, "2027", BudgetType.OriginalApproved, "BM", [new NewBudgetLine("6000", 1, 50m)])).Value;
        IncrementalBudgetRequest Build(long source) => new()
        {
            CompanyId = companyId, BudgetId = draft, Basis = IncrementalBasis.PriorYearActual, SourceFiscalYearId = source, UpliftPercent = 10m,
        };
        (await methods.BuildIncrementalAsync(Build(nextYear))).Error.Code.Should().Be("Budget.SourceYear");
        (await methods.BuildIncrementalAsync(Build(thisYear))).Value.Should().Be(1);
        (await db.Set<BudgetLine>().AsNoTracking().SingleAsync(l => l.BudgetId == draft && l.AccountCode == "4000")).Should()
            .Match<BudgetLine>(l => l.Period == 1 && l.Amount == -1_100m, "January's sales of 1,000 (a credit) plus 10%");

        // This year's budget: sales of 900 and costs of 100 a month.
        long budget = (await budgeting.CreateBudgetAsync(companyId, thisYear, "2026", BudgetType.OriginalApproved, "BM",
            [.. Enumerable.Range(1, 12).SelectMany(p => new[] { new NewBudgetLine("4000", p, -900m), new NewBudgetLine("6000", p, 100m) })])).Value;
        long reforecast = (await methods.ReforecastAsync(companyId, budget, 1, "2026 after January")).Value;
        Budget outturn = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == reforecast);
        outturn.Type.Should().Be(BudgetType.Reforecast);
        outturn.Lines.Where(l => l.AccountCode == "4000").Sum(l => l.Amount).Should().Be(-1_000m - 11 * 900m, "January's actual sales and the budget for the rest");
        outturn.Lines.Where(l => l.AccountCode == "6000").Sum(l => l.Amount).Should().Be(11 * 100m, "no January costs were booked");

        // The cash budget: sales collected half in the month, half the month after.
        var cash = (await methods.CashBudgetAsync(companyId, budget, [new CashTiming("4", [50m, 50m])], 1_000m)).Value;
        (cash.Months[0].Receipts, cash.Months[0].Payments, cash.Months[0].Closing).Should().Be((450m, 100m, 1_350m));
        cash.Months[1].Receipts.Should().Be(900m);
        cash.CarryOut.Should().Be(450m, "half of December's sales come in next year");
        (await methods.CashBudgetAsync(companyId, budget, [new CashTiming("4", [50m])], 0m)).Error.Code.Should().Be("Budget.CashTiming");
    }
}
