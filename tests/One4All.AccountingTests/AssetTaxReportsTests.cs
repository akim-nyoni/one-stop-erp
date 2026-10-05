using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the FA- and TX-numbered reports (brief §14 M, N): a vehicle registered, depreciated for two months and moved to a
/// new location; the depreciation schedule, additions and transfers, and the movement note read it back; every FA / TX
/// report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetTaxReportsTests
{
    private readonly AccountingFixture _fixture;

    public AssetTaxReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Asset_and_tax_reports_reflect_the_register_and_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        long truck = (await fa.RegisterAsync(new RegisterAssetRequest
        {
            CompanyId = companyId, AssetNumber = "ATR-VEH", Description = "Truck", AssetClass = "Vehicles", Cost = 12_000m, ResidualValue = 0m, AcquisitionDate = new DateOnly(2026, 1, 1),
            Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
            FundingAccountCode = "1100", DocumentNumber = "FA-ATR",
        })).Value;
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 1, 31), "DEP-ATR-01")).IsSuccess.Should().BeTrue();
        (await fa.RunDepreciationAsync(companyId, new DateOnly(2026, 2, 28), "DEP-ATR-02")).IsSuccess.Should().BeTrue();
        (await fa.TransferAsync(companyId, truck, "Depot B", "J. Moyo")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("FA207")).Should().Contain("2026-01,ATR-VEH,Truck,Vehicles,1000").And.Contain("2026-02,ATR-VEH,Truck,Vehicles,1000");
        (await Render("FA208")).Should().Contain("Addition,ATR-VEH,Truck,Vehicles,2026-01-01").And.Contain("Transfer,ATR-VEH,Truck,Vehicles");
        (await Render("FA209")).Should().Contain("Vehicles,0.00,\"12,000.00\",0.00,\"12,000.00\",0.00,\"2,000.00\",0.00,\"2,000.00\",10000");

        foreach (string code in AssetTaxReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
