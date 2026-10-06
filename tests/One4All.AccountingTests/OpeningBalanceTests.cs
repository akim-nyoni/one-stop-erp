using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.MasterData;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers migrating a company: master data previewed then committed, opening stock, open receivables (with a credit) and payables, and
/// the legacy trial balance — after which the suspense account nets to zero and the controls agree with their sub-ledgers.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OpeningBalanceTests
{
    private readonly AccountingFixture _fixture;

    public OpeningBalanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_legacy_company_is_migrated_and_reconciles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await Scenario.SeedAsync(db);
        long companyId = (await scope.ServiceProvider.GetRequiredService<IIndustrySetupService>().SetUpCompanyAsync(new IndustrySetupRequest
        {
            Code = "OB" + Guid.NewGuid().ToString("N")[..8], Name = "Legacy Co", BaseCurrency = Scenario.Usd, Verticals = [], FiscalYearStart = new DateOnly(2026, 1, 1),
        })).Value.CompanyId;
        IOpeningBalanceService ob = scope.ServiceProvider.GetRequiredService<IOpeningBalanceService>();
        DateOnly opening = new(2026, 1, 1);

        // A file with a bad row is previewed and refused whole; fixed, it commits.
        ImportReport bad = await ob.ImportCustomersAsync(companyId, "code,name,currency,credit_limit\nC1,First,USD,500\nC2,Second,,0\nc1,Again,USD,0\n", commit: true);
        bad.Committed.Should().BeFalse();
        bad.Errors.Select(e => e.LineNumber).Should().Equal(3, 4);
        db.Customers.Count(c => c.CompanyId == companyId).Should().Be(0);
        (await ob.ImportCustomersAsync(companyId, "code,name,currency,credit_limit\nC1,First,USD,500\nC2,Second,USD,0\n", commit: false)).Should().Match<ImportReport>(r => !r.Committed && r.Errors.Count == 0);
        (await ob.ImportCustomersAsync(companyId, "code,name,currency,credit_limit\nC1,First,USD,500\nC2,Second,USD,0\n", commit: true)).Created.Should().Be(2);
        (await ob.ImportCustomersAsync(companyId, "code,name,currency\nC1,First,USD\n", commit: true)).Skipped.Should().Be(1);
        (await ob.ImportSuppliersAsync(companyId, "code,name,currency\nS1,\"Supplier, One\",USD\n", commit: true)).Created.Should().Be(1);
        (await ob.ImportItemsAsync(companyId, "code,description,type,revenue_account,cost_account,category\nSOAP,Soap bar,Stock,4000,5000,Cleaning\nBAD,Bad,Stock,9999,,\n", commit: true))
            .Errors.Should().ContainSingle(e => e.LineNumber == 3);
        (await ob.ImportItemsAsync(companyId, "code,description,type,revenue_account,cost_account,category\nSOAP,Soap bar,Stock,4000,5000,Cleaning\n", commit: true)).Created.Should().Be(1);

        // Opening balances against the 2900 suspense account.
        (await ob.ImportStockAsync(companyId, "item,warehouse,quantity,unit_cost\nSOAP,MAIN,10,5\n", opening, "2900", commit: true)).Created.Should().Be(1);
        (await ob.ImportOpenItemsAsync(companyId, TradingLedger.AccountsReceivable, "trader,document,date,amount\nC1,INV-OLD1,2026-01-01,300\nC2,CN-OLD1,2026-01-01,-50\n", "2900", commit: true))
            .Created.Should().Be(2);
        (await ob.ImportOpenItemsAsync(companyId, TradingLedger.AccountsPayable, "trader,document,date,amount\nS1,BILL-1,2026-01-01,120\n", "2900", commit: true)).Created.Should().Be(1);
        (await ob.ImportTrialBalanceAsync(companyId, "account,debit,credit\n1100,1000,0\n3000,0,1000.01\n", opening, "2900", commit: false)).Errors.Should().Contain(e => e.Message.Contains("does not balance"));
        string trialBalance = "account,debit,credit\n1100,1000,0\n1200,250,0\n1400,50,0\n2100,0,120\n3000,0,1180\n";
        (await ob.ImportTrialBalanceAsync(companyId, trialBalance, opening, "2900", commit: true)).Committed.Should().BeTrue();
        (await ob.ImportTrialBalanceAsync(companyId, trialBalance, opening, "2900", commit: true)).Errors.Should().Contain(e => e.Message.Contains("already been loaded"));

        OpeningBalanceReconciliation reconciliation = (await ob.ReconcileAsync(companyId, "2900")).Value;
        reconciliation.SuspenseBalance.Should().Be(0m);
        reconciliation.Controls.Should().OnlyContain(c => c.Agrees);
        reconciliation.Ready.Should().BeTrue();
    }
}
