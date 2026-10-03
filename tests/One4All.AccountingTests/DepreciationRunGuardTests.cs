using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the depreciation run guards: an asset acquired after the run month is not charged, re-running a
/// month posts nothing, and the next month charges again — with accumulated depreciation equal to the ledger.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DepreciationRunGuardTests
{
    private readonly AccountingFixture _fixture;

    public DepreciationRunGuardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runs_skip_future_assets_and_never_charge_a_month_twice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            // Van bought in January (1,000/month); laptop bought in March (100/month).
            (await fa.RegisterAsync(Asset(companyId, "DG-VAN", new DateOnly(2026, 1, 10), 24_000m, 24))).IsSuccess.Should().BeTrue();
            (await fa.RegisterAsync(Asset(companyId, "DG-LAP", new DateOnly(2026, 3, 15), 1_200m, 12))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();

            // February: only the van; the laptop is not yet acquired.
            DepreciationRunResult feb = (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 2, 28), "DG-FEB")).Value;
            feb.AssetsDepreciated.Should().Be(1);
            feb.TotalDepreciation.Should().Be(1_000m);
            feb.SkippedNotYetAcquired.Should().Be(1);

            // Running February again posts nothing.
            DepreciationRunResult febAgain = (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 2, 15), "DG-FEB2")).Value;
            febAgain.AssetsDepreciated.Should().Be(0);
            febAgain.JournalNumber.Should().BeNull();
            febAgain.SkippedAlreadyDepreciated.Should().Be(1);
            febAgain.SkippedNotYetAcquired.Should().Be(1);

            // March: both (the laptop from its acquisition month).
            DepreciationRunResult mar = (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 3, 31), "DG-MAR")).Value;
            mar.AssetsDepreciated.Should().Be(2);
            mar.TotalDepreciation.Should().Be(1_100m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            decimal accumulated = db.Set<FixedAsset>().Where(a => a.CompanyId == companyId).Sum(a => a.AccumulatedDepreciation);
            accumulated.Should().Be(2_100m);

            // The ledger agrees: accumulated depreciation (1650) carries the same 2,100 credit.
            IConsolidationService tb = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
            ConsolidatedTrialBalance balance = (await tb.ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            balance.IsBalanced.Should().BeTrue();
            balance.Rows.Single(r => r.AccountCode == "1650").Credit.Should().Be(2_100m);
        }
    }

    private static RegisterAssetRequest Asset(long companyId, string number, DateOnly acquired, decimal cost, int lifeMonths) => new()
    {
        CompanyId = companyId, AssetNumber = number, Description = number, AssetClass = "Equipment",
        Cost = cost, ResidualValue = 0m, AcquisitionDate = acquired, Method = DepreciationMethod.StraightLine, UsefulLifeMonths = lifeMonths,
        CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
        FundingAccountCode = "1100", DocumentNumber = $"REG-{number}",
    };
}
