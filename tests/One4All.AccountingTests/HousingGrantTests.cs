using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers IAS 20 housing grants: a 250,000 grant towards a 1,000,000 servicing budget is received into deferred income;
/// 400,000 spent while the conditions are not yet assured recognises nothing; once assured, 100,000 is recognised; at
/// 700,000 spent the movement of 75,000 follows; expenditure cannot go down; the disclosure shows 75,000 deferred. A
/// second grant with unfulfilled conditions is disclosed in full as deferred.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingGrantTests
{
    private readonly AccountingFixture _fixture;

    public HousingGrantTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Grants_are_deferred_then_recognised_with_expenditure()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2270", "Deferred Grant Income", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "4990", "Housing Grant Income", AccountType.Income));
        await db.SaveChangesAsync();

        IHousingGrantService grants = scope.ServiceProvider.GetRequiredService<IHousingGrantService>();
        GrantReceipt Receipt(string grantor, decimal amount) => new()
        {
            CompanyId = companyId, Grantor = grantor, Amount = amount, BudgetedExpenditure = 1000000m, Conditions = "Serviced stands allocated to first-time buyers by December 2027",
            ReceivedOn = new DateOnly(2026, 2, 1), BankAccountCode = "1100", DeferredAccountCode = "2270", IncomeAccountCode = "4990",
        };

        string grant = (await grants.ReceiveAsync(Receipt("Ministry of Housing", 250000m))).Value;
        string pending = (await grants.ReceiveAsync(Receipt("Development Bank", 50000m))).Value;

        (await grants.RecordExpenditureAsync(companyId, grant, 400000m, new DateOnly(2026, 4, 30))).Value.Should().Be(0m);
        (await grants.AssureConditionsAsync(companyId, grant, new DateOnly(2026, 5, 15))).Value.Should().Be(100000m);
        (await grants.AssureConditionsAsync(companyId, grant, new DateOnly(2026, 5, 16))).Error.Code.Should().Be("Hse.ConditionsAssured");
        (await grants.RecordExpenditureAsync(companyId, grant, 700000m, new DateOnly(2026, 6, 30))).Value.Should().Be(175000m);
        (await grants.RecordExpenditureAsync(companyId, grant, 600000m, new DateOnly(2026, 7, 31))).Error.Code.Should().Be("Hse.ExpenditureToDate");

        (await grants.DisclosureAsync(companyId)).Select(g => (g.GrantNumber, g.Recognised, g.Deferred, g.ConditionsAssured)).Should().Equal(
            (grant, 175000m, 75000m, true), (pending, 0m, 50000m, false));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 7, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4990").Credit.Should().Be(175000m);
        tb.Rows.Single(r => r.AccountCode == "2270").Credit.Should().Be(125000m); // 75,000 + 50,000 still deferred
    }
}
