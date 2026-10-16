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
public sealed class AssetDisposalReportTests
{
    private readonly AccountingFixture _fixture;

    public AssetDisposalReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Disposing_an_asset_records_it_for_the_disposals_report()
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
            var reg = await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = "DISP-1", Description = "Old laptop", AssetClass = "IT",
                Cost = 1_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 10,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FA-D1",
            });
            reg.IsSuccess.Should().BeTrue(reg.IsFailure ? reg.Error.ToString() : null);
            assetId = reg.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // NBV is 1,000 (no depreciation run); sell for 1,200 → 200 gain.
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
            var disposal = await fa.DisposeAsync(companyId, assetId, 1_200m, new DateOnly(2026, 2, 1), "1100", "7300", "7400", "DISP-J1");
            disposal.IsSuccess.Should().BeTrue(disposal.IsFailure ? disposal.Error.ToString() : null);
            disposal.Value.GainOrLoss.Should().Be(400m, "January and February (100 a month) are charged by the disposal, leaving a carrying amount of 800");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("AssetDisposals"));
            var rows = await vertical.GetRowsAsync(companyId, "AssetDisposals", new Dictionary<string, object?>());
            rows.Should().ContainSingle(r => (string)r["AssetNumber"]! == "DISP-1");
            rows.Single()["Proceeds"].Should().Be(1_200m);
            rows.Single()["GainOrLoss"].Should().Be(400m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "FA-DISP", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("DISP-1").And.Contain("1,200.00").And.Contain("400.00");
        }
    }
}
