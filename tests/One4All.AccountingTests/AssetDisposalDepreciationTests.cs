using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers brief §4.14: a disposal charges depreciation up to the disposal date (the months not yet run, and the disposal month by the convention)
/// before clearing the asset, so the gain or loss is on the carrying amount at the date; and the half-year convention charges six months in the
/// acquisition year whatever the month.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetDisposalDepreciationTests
{
    private readonly AccountingFixture _fixture;

    public AssetDisposalDepreciationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Disposal_charges_depreciation_to_the_date_and_the_half_year_convention_spreads_six_months()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        RegisterAssetRequest Asset(string number, DateOnly acquired, DepreciationConvention convention) => new()
        {
            CompanyId = companyId, AssetNumber = number, Description = number, AssetClass = "Vehicles", Cost = 1_200m, ResidualValue = 0m, AcquisitionDate = acquired,
            Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
            FundingAccountCode = "1100", DocumentNumber = $"ACQ-{number}", Convention = convention,
        };

        // 100 a month; January was run, then the van goes on 20 March: February and March (a full month under the full-month convention) are charged in the disposal.
        long van = (await fa.RegisterAsync(Asset("DD-VAN", new DateOnly(2026, 1, 10), DepreciationConvention.FullMonth))).Value;
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 1, 31), "DEP-DD-01")).Value.TotalDepreciation.Should().Be(100m);
        DisposalResult sold = (await fa.DisposeAsync(companyId, van, 950m, new DateOnly(2026, 3, 20), "1100", "4900", "6900", "DSP-DD-VAN")).Value;
        FixedAsset gone = await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == van);
        (gone.AccumulatedDepreciation, gone.Status).Should().Be((300m, FixedAssetStatus.Disposed));
        sold.GainOrLoss.Should().Be(950m - 900m, "proceeds against a carrying amount of 1,200 − 300");
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "DSP-DD-VAN").SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "6000").SumAsync(l => l.BaseDebit)).Should().Be(200m, "February and March charged in the disposal journal");

        // Half-year convention: a van bought in October with a 12-month life carries 600 over October to December (200 a month), then 100 a month.
        long late = (await fa.RegisterAsync(Asset("DD-LATE", new DateOnly(2026, 10, 5), DepreciationConvention.MidYear))).Value;
        FixedAsset lateVan = await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == late);
        lateVan.MonthlyDepreciation(0m, new DateOnly(2026, 10, 31)).Should().Be(200m);
        lateVan.MonthlyDepreciation(0m, new DateOnly(2027, 1, 31)).Should().Be(100m);
    }
}
