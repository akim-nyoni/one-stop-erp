using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class FixedAssetRevaluationTests
{
    private readonly AccountingFixture _fixture;

    public FixedAssetRevaluationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Revaluation_uplifts_to_reserve_and_write_downs_hit_impairment_expense()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long assetId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            assetId = (await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "LAND-1", Description = "Freehold land", AssetClass = "Property",
                Cost = 100_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 0, // land is not depreciated
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FA-L1",
            })).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();

            // Uplift to 130,000 → +30,000 to the revaluation reserve.
            var up = await fa.RevalueAsync(companyId, assetId, 130_000m, new DateOnly(2026, 6, 30), "3300", "6500", "REV-1");
            up.IsSuccess.Should().BeTrue(up.IsFailure ? up.Error.ToString() : null);
            up.Value.Delta.Should().Be(30_000m);
            up.Value.NewCarryingAmount.Should().Be(130_000m);

            // Later write-down to 120,000 → −10,000 impairment expense.
            var down = await fa.RevalueAsync(companyId, assetId, 120_000m, new DateOnly(2026, 9, 30), "3300", "6500", "REV-2");
            down.IsSuccess.Should().BeTrue(down.IsFailure ? down.Error.ToString() : null);
            down.Value.Delta.Should().Be(-10_000m);

            // A negative carrying amount is refused.
            (await fa.RevalueAsync(companyId, assetId, -1m, new DateOnly(2026, 9, 30), "3300", "6500", "REV-X")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<FixedAsset>().FirstAsync(a => a.Id == assetId)).NetBookValue.Should().Be(120_000m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 12, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1600" && r.Debit == 120_000m); // asset cost after revaluations
            tb.Rows.Should().Contain(r => r.AccountCode == "3300" && r.Credit == 30_000m); // revaluation reserve (uplift)
            tb.Rows.Should().Contain(r => r.AccountCode == "6500" && r.Debit == 10_000m);  // impairment loss (write-down)
        }
    }
}
