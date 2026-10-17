using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers forecast timing: an asset bought later in the window is only forecast from its acquisition month,
/// and a month the run has already charged is not forecast again - so the forecast agrees with what the runs
/// will actually post.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DepreciationForecastTimingTests
{
    private readonly AccountingFixture _fixture;

    public DepreciationForecastTimingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Forecast_respects_acquisition_month_and_months_already_charged()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            // Van from January at 1,000/month; forklift from April at 500/month.
            (await fa.RegisterAsync(Asset(companyId, "FT-VAN", new DateOnly(2026, 1, 5), 24_000m, 24))).IsSuccess.Should().BeTrue();
            (await fa.RegisterAsync(Asset(companyId, "FT-FORK", new DateOnly(2026, 4, 20), 6_000m, 12))).IsSuccess.Should().BeTrue();

            // January and February have already been run for the van.
            (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 1, 31), "FT-JAN")).Value.TotalDepreciation.Should().Be(1_000m);
            (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 2, 28), "FT-FEB")).Value.TotalDepreciation.Should().Be(1_000m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            var forecast = (await fa.DepreciationForecastAsync(companyId, new DateOnly(2026, 2, 1), 4)).Value.Periods;

            forecast.Select(p => p.Period).Should().Equal("2026-02", "2026-03", "2026-04", "2026-05");
            forecast[0].Charge.Should().Be(0m);      // February already charged; the forklift not yet bought
            forecast[1].Charge.Should().Be(1_000m);  // van only
            forecast[2].Charge.Should().Be(1_500m);  // forklift from its acquisition month
            forecast[3].Charge.Should().Be(1_500m);
            forecast[0].ClosingNetBookValue.Should().Be(22_000m + 6_000m);
            forecast[3].ClosingNetBookValue.Should().Be(22_000m + 6_000m - 4_000m);

            // And the forecast for March matches what the March run then posts.
            (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 3, 31), "FT-MAR")).Value.TotalDepreciation.Should().Be(forecast[1].Charge);
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
