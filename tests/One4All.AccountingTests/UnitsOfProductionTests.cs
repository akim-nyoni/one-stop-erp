using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers units-of-production depreciation: the asset needs its expected total units; each month is charged on that month's usage
/// readings (nothing when idle), never below residual; readings for a month already charged are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class UnitsOfProductionTests
{
    private readonly AccountingFixture _fixture;

    public UnitsOfProductionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Assets_are_depreciated_on_the_units_they_produce()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        RegisterAssetRequest Truck(decimal? units) => new()
        {
            CompanyId = companyId, AssetNumber = "UOP-1", Description = "Tipper truck", AssetClass = "Vehicles", Cost = 110_000m, ResidualValue = 10_000m, AcquisitionDate = new DateOnly(2026, 1, 1),
            Method = DepreciationMethod.UnitsOfProduction, TotalEstimatedUnits = units, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
            FundingAccountCode = "1100", DocumentNumber = "UOP-ACQ",
        };

        (await fa.RegisterAsync(Truck(null))).Error.Code.Should().Be("Fa.TotalUnits");
        long truck = (await fa.RegisterAsync(Truck(200_000m))).Value;

        // January: 5,000 km of 200,000 → 100,000 × 2.5% = 2,500. February: idle, no charge.
        (await fa.RecordUsageAsync(companyId, truck, new DateOnly(2026, 1, 15), 3_000m, "Odometer")).IsSuccess.Should().BeTrue();
        (await fa.RecordUsageAsync(companyId, truck, new DateOnly(2026, 1, 31), 2_000m, "Odometer")).IsSuccess.Should().BeTrue();
        (await fa.RecordUsageAsync(companyId, truck, new DateOnly(2026, 1, 31), 0m, "Odometer")).Error.Code.Should().Be("Fa.UsageUnits");
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 1, 31), "DEP-UOP-01")).Value.TotalDepreciation.Should().Be(2_500m);
        (await fa.RecordUsageAsync(companyId, truck, new DateOnly(2026, 1, 31), 100m, "Late")).Error.Code.Should().Be("Fa.PeriodDepreciated");
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 2, 28), "DEP-UOP-02")).Value.TotalDepreciation.Should().Be(0m);

        // March: a reading beyond the remaining life is capped at the residual value.
        (await fa.RecordUsageAsync(companyId, truck, new DateOnly(2026, 3, 20), 250_000m, "Odometer")).IsSuccess.Should().BeTrue();
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 3, 31), "DEP-UOP-03")).Value.TotalDepreciation.Should().Be(97_500m);
        FixedAsset after = await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == truck);
        after.NetBookValue.Should().Be(10_000m);
        after.Status.Should().Be(FixedAssetStatus.FullyDepreciated);
    }
}
