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
public sealed class AssetMaintenanceTests
{
    private readonly AccountingFixture _fixture;

    public AssetMaintenanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Maintenance_events_are_logged_and_listed()
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
                CompanyId = companyId, AssetNumber = "VAN-9", Description = "Delivery van", AssetClass = "Vehicles",
                Cost = 30_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 60,
                CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = "FA-MNT-1",
            });
            reg.IsSuccess.Should().BeTrue(reg.IsFailure ? reg.Error.ToString() : null);
            assetId = reg.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();

            (await fa.RecordMaintenanceAsync(companyId, assetId, new DateOnly(2026, 3, 1), "   ", 50m, "Garage")).IsFailure.Should().BeTrue();
            (await fa.RecordMaintenanceAsync(companyId, assetId, new DateOnly(2026, 3, 1), "Service", -5m, "Garage")).IsFailure.Should().BeTrue();
            (await fa.RecordMaintenanceAsync(companyId, 999_999, new DateOnly(2026, 3, 1), "Service", 50m, "Garage")).IsFailure.Should().BeTrue();

            (await fa.RecordMaintenanceAsync(companyId, assetId, new DateOnly(2026, 3, 1), "10,000km service", 150m, "AutoFix")).IsSuccess.Should().BeTrue();
            (await fa.RecordMaintenanceAsync(companyId, assetId, new DateOnly(2026, 6, 1), "Brake pads", 220m, "AutoFix")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "FA-MAINT", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("VAN-9").And.Contain("Brake pads").And.Contain("220.00").And.Contain("AutoFix");
        }
    }
}
