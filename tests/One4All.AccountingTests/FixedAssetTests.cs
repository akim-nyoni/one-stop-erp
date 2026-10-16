using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class FixedAssetTests
{
    private readonly AccountingFixture _fixture;

    public FixedAssetTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Register_depreciate_and_dispose_with_gain()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        long assetId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            // Cost 24,000, residual 0, straight line over 24 months → 1,000/month.
            var reg = await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "VH-1", Description = "Delivery van", AssetClass = "Vehicles",
                Cost = 24_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 24,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FA-1",
            });
            reg.IsSuccess.Should().BeTrue(reg.IsFailure ? reg.Error.ToString() : null);
            assetId = reg.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            for (int m = 1; m <= 3; m++)
            {
                var run = await fa.RunDepreciationAsync(companyId, new DateOnly(2026, m, 28), $"DEP-{m}");
                run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
                run.Value.TotalDepreciation.Should().Be(1_000m);
            }
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            // After 3 months accumulated is 3,000; the disposal on 1 April charges April too (a full month under the full-month convention), so the
            // carrying amount at the date is 20,000. Sell for 22,000 → gain 2,000.
            var disp = await fa.DisposeAsync(companyId, assetId, 22_000m, new DateOnly(2026, 4, 1), "1100", "7300", "7400", "DISP-1");
            disp.IsSuccess.Should().BeTrue(disp.IsFailure ? disp.Error.ToString() : null);
            disp.Value.NetBookValue.Should().Be(20_000m);
            disp.Value.GainOrLoss.Should().Be(2_000m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // Cost and accumulated depreciation fully cleared on disposal.
            tb.Rows.Should().NotContain(r => r.AccountCode == "1600" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().Contain(r => r.AccountCode == "7300" && r.Credit == 2_000m);
            tb.Rows.Should().Contain(r => r.AccountCode == "6000" && r.Debit == 4_000m, "three runs plus the disposal month charged in the disposal journal");
        }
    }
}
