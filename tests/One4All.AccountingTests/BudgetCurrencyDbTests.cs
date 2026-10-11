using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a budget prepared in a foreign currency: the header holds the currency and budget rate, a new rate re-states the base equivalents,
/// a missing rate is refused, the base currency clears it, and an activated (sealed) budget cannot change currency.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetCurrencyDbTests
{
    private readonly AccountingFixture _fixture;

    public BudgetCurrencyDbTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_budget_is_prepared_in_a_currency_at_a_budget_rate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgets = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgets.CreateBudgetAsync(companyId, yearId, "ZAR plan", BudgetType.OriginalApproved, "BCY", [new NewBudgetLine("6000", 1, 1000m)])).Value;
        CurrencyCode zar = CurrencyCode.Parse("ZAR");

        (await budgets.SetBudgetCurrencyAsync(companyId, budgetId, zar, null)).Error.Code.Should().Be("Budget.PlannedRate");
        (await budgets.SetBudgetCurrencyAsync(companyId, budgetId, zar, 0.05m)).IsSuccess.Should().BeTrue();
        (await budgets.SetBudgetCurrencyAsync(companyId, budgetId, zar, 0.055m)).IsSuccess.Should().BeTrue();
        Budget saved = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId);
        (saved.Currency, saved.BudgetRate).Should().Be(((CurrencyCode?)zar, (decimal?)0.055m));
        saved.Lines.Single().Amount.Should().Be(1100m, "20,000 rand re-stated at the new rate");

        (await budgets.SetBudgetCurrencyAsync(companyId, budgetId, Scenario.Usd, null)).IsSuccess.Should().BeTrue("the base currency clears it");
        (await db.Set<Budget>().AsNoTracking().SingleAsync(b => b.Id == budgetId)).Currency.Should().BeNull();

        (await budgets.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();
        (await budgets.SetBudgetCurrencyAsync(companyId, budgetId, zar, 0.05m)).Error.Code.Should().Be("Budget.Sealed");
    }
}
