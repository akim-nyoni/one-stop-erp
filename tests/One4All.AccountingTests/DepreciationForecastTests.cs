using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class DepreciationForecastTests
{
    private readonly AccountingFixture _fixture;

    public DepreciationForecastTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Forecast_projects_straight_line_depreciation_and_stops_at_residual()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            // Cost 12,000, residual 0, straight line over 12 months → 1,000/month, fully depreciated after 12.
            var reg = await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "PC-1", Description = "Server", AssetClass = "IT",
                Cost = 12_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FAF-1",
            });
            reg.IsSuccess.Should().BeTrue(reg.IsFailure ? reg.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();

            (await fa.DepreciationForecastAsync(companyId, new DateOnly(2026, 2, 1), 0)).IsFailure.Should().BeTrue(); // months must be positive

            var forecast = await fa.DepreciationForecastAsync(companyId, new DateOnly(2026, 2, 1), 14);
            forecast.IsSuccess.Should().BeTrue(forecast.IsFailure ? forecast.Error.ToString() : null);
            var periods = forecast.Value.Periods;

            periods.Should().HaveCount(14);
            periods[0].Period.Should().Be("2026-02");
            periods[0].Charge.Should().Be(1_000m);
            periods[0].ClosingNetBookValue.Should().Be(11_000m);
            periods[11].Charge.Should().Be(1_000m);            // 12th month drives NBV to zero
            periods[11].ClosingNetBookValue.Should().Be(0m);
            periods[12].Charge.Should().Be(0m);                // nothing left to depreciate
            periods[12].ClosingNetBookValue.Should().Be(0m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["StartMonth"] = new DateOnly(2026, 2, 1), ["Months"] = 3 };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "FA-FCST", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("2026-02").And.Contain("1,000.00");
        }
    }
}
