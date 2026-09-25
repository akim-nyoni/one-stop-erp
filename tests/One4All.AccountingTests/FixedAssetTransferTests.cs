using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class FixedAssetTransferTests
{
    private readonly AccountingFixture _fixture;

    public FixedAssetTransferTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transfer_updates_location_and_custodian()
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
                CompanyId = companyId, AssetNumber = "FT-1", Description = "Laptop", AssetClass = "IT",
                Cost = 1_200m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 36,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FT-A1",
            })).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            (await fa.TransferAsync(companyId, assetId, null, null)).IsFailure.Should().BeTrue(); // nothing to change
            (await fa.TransferAsync(companyId, assetId, "Branch B", "Jane Custodian")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            FixedAsset asset = await db.Set<FixedAsset>().FirstAsync(a => a.Id == assetId);
            asset.Location.Should().Be("Branch B");
            asset.Custodian.Should().Be("Jane Custodian");

            // Custodian-only transfer leaves the location unchanged.
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            (await fa.TransferAsync(companyId, assetId, null, "Sam New")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            FixedAsset asset = await db.Set<FixedAsset>().FirstAsync(a => a.Id == assetId);
            asset.Location.Should().Be("Branch B");     // unchanged
            asset.Custodian.Should().Be("Sam New");     // updated
        }
    }
}
