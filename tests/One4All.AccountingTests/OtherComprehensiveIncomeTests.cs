using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A 200 revaluation surplus on a reserve marked as other comprehensive income shows under the profit and makes total comprehensive income.</summary>
[Collection(AccountingCollection.Name)]
public sealed class OtherComprehensiveIncomeTests
{
    private readonly AccountingFixture _fixture;

    public OtherComprehensiveIncomeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reserve_movements_are_other_comprehensive_income()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateAccountAsync(new NewAccount(companyId, "3350", "Revaluation Surplus", AccountType.Equity,
            IsOtherComprehensiveIncome: true))).IsSuccess.Should().BeTrue();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Journal(string debit, string credit, decimal amount) => new()
        {
            CompanyId = companyId, PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, Narrative = "OCI test",
            Lines = [new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount }],
        };
        (await engine.PostAsync(Journal("1100", "4000", 500m))).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Journal("1600", "3350", 200m))).IsSuccess.Should().BeTrue();

        IncomeStatement statement = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().IncomeStatementAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        (statement.NetProfit, statement.TotalOtherComprehensiveIncome, statement.TotalComprehensiveIncome).Should().Be((500m, 200m, 700m));
        statement.Oci.Single().AccountCode.Should().Be("3350");
    }
}
