using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers asset classes and impairment: an asset registered in a class takes its method, life, residual and accounts; an asset with no
/// class and no accounts is refused; an impairment books the loss within accumulated depreciation and straight-line depreciation then
/// spreads the new carrying amount over the life left; a recovery reverses the loss only up to the carrying amount the asset would have had
/// unimpaired; every test is recorded.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetClassImpairmentTests
{
    private readonly AccountingFixture _fixture;

    public AssetClassImpairmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Classes_default_assets_and_impairment_is_booked_and_reversed_within_limits()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "1491", "Vehicles at cost", AccountType.Asset), new LedgerAccount(companyId, "1492", "Vehicles depreciation", AccountType.Asset),
            new LedgerAccount(companyId, "6191", "Depreciation", AccountType.Expense), new LedgerAccount(companyId, "6192", "Impairment", AccountType.Expense));
        await db.SaveChangesAsync();
        IFixedAssetService assets = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();

        (await assets.SaveClassAsync(new AssetClassRequest(companyId, "VEH", "Vehicles", DepreciationMethod.StraightLine, 0, 10m, 0m, "1491", "1492", "6191", "6192"))).Error.Code.Should().Be("Fa.Class");
        (await assets.SaveClassAsync(new AssetClassRequest(companyId, "VEH", "Vehicles", DepreciationMethod.StraightLine, 60, 10m, 0m, "1491", "1492", "9999", "6192"))).Error.Code.Should().Be("Fa.AccountNotFound");
        (await assets.SaveClassAsync(new AssetClassRequest(companyId, "veh", "Vehicles", DepreciationMethod.StraightLine, 60, 10m, 0m, "1491", "1492", "6191", "6192"))).IsSuccess.Should().BeTrue();

        RegisterAssetRequest Truck(string number, string assetClass) => new()
        {
            CompanyId = companyId, AssetNumber = number, Description = "Truck", AssetClass = assetClass, Cost = 6_000m, AcquisitionDate = new DateOnly(2026, 1, 1), FundingAccountCode = "1100",
            DocumentNumber = $"ACQ-{number}",
        };
        (await assets.RegisterAsync(Truck("CI-0", "NONE"))).Error.Code.Should().Be("Fa.ClassDefaults");
        long truck = (await assets.RegisterAsync(Truck("CI-1", "Veh"))).Value;
        FixedAsset registered = await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == truck);
        (registered.Method, registered.UsefulLifeMonths, registered.ResidualValue, registered.CostAccountCode, registered.DepreciationExpenseAccountCode)
            .Should().Be((DepreciationMethod.StraightLine, 60, 600m, "1491", "6191"));

        // Three months at 90 (5,400 over 60), then impaired from 5,730 to 4,000.
        foreach (int month in new[] { 1, 2, 3 })
        {
            (await assets.RunDepreciationAsync(companyId, new DateOnly(2026, month, 28), $"CI-DEP-{month}")).Value.TotalDepreciation.Should().Be(90m);
        }

        (await assets.ImpairAsync(companyId, truck, 4_000m, new DateOnly(2026, 3, 31), " ", "CI-IMP-1")).Error.Code.Should().Be("Fa.Impairment");
        ImpairmentResult loss = (await assets.ImpairAsync(companyId, truck, 4_000m, new DateOnly(2026, 3, 31), "Accident damage", "CI-IMP-1")).Value;
        (loss.Amount, loss.CarryingAmount).Should().Be((1_730m, 4_000m));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "CI-IMP-1").SelectMany(j => j.Lines).Where(l => l.AccountCode == "6192").SumAsync(l => l.BaseDebit))
            .Should().Be(1_730m);

        // April: (4,000 − 600) over the 57 months left.
        decimal april = (await assets.RunDepreciationAsync(companyId, new DateOnly(2026, 4, 30), "CI-DEP-4")).Value.TotalDepreciation;
        april.Should().BeApproximately(3_400m / 57m, 0.01m);

        // Recovery to 9,000: reversed only up to the unimpaired carrying amount (6,000 − 4 × 90 = 5,640).
        ImpairmentResult reversal = (await assets.ImpairAsync(companyId, truck, 9_000m, new DateOnly(2026, 4, 30), "Repaired, market recovered", "CI-IMP-2")).Value;
        reversal.CarryingAmount.Should().Be(5_640m);
        reversal.Amount.Should().Be(-(5_640m - (4_000m - april)));
        (await db.Set<AssetImpairment>().AsNoTracking().CountAsync(x => x.FixedAssetId == truck)).Should().Be(2);
        (await assets.ImpairAsync(companyId, truck, 9_000m, new DateOnly(2026, 4, 30), "Tested again", "CI-IMP-3")).Value.Amount.Should().Be(0m);
    }
}
