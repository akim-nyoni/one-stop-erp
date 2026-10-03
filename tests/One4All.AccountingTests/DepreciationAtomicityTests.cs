using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.FixedAssets;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a depreciation run refused by a closed GL period: the assets are not marked as depreciated for the month,
/// even if the scope saves afterwards, so once the period is reopened the month is charged rather than skipped.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DepreciationAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public DepreciationAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_refused_run_does_not_mark_the_month_as_charged()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long yearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();

            (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "DA-VAN", Description = "Van", AssetClass = "Vehicles",
                Cost = 12_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 5), Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "REG-DA-VAN",
            })).IsSuccess.Should().BeTrue();

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 2, "GL", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        }

        DateOnly february = new(2026, 2, 28);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RunDepreciationAsync(companyId, february, "DEP-2602")).IsFailure.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            FixedAsset van = await db.Set<FixedAsset>().AsNoTracking().SingleAsync(a => a.CompanyId == companyId);
            van.LastDepreciatedPeriod.Should().BeNull();
            van.AccumulatedDepreciation.Should().Be(0m);

            (await scope.ServiceProvider.GetRequiredService<IPeriodService>().SetStatusAsync(companyId, yearId, 2, "GL", PeriodStatus.Open)).IsSuccess.Should().BeTrue();
            DepreciationRunResult run = (await scope.ServiceProvider.GetRequiredService<IFixedAssetService>().RunDepreciationAsync(companyId, february, "DEP-2602")).Value;
            run.AssetsDepreciated.Should().Be(1);
            run.TotalDepreciation.Should().Be(1_000m);
        }
    }
}
