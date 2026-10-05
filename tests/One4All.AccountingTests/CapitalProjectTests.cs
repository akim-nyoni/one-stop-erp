using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers capital work in progress (brief §14 M 211): costs post into CWIP, capitalisation registers the asset at their total and
/// clears CWIP, and FA211 rolls the project forward before and after.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CapitalProjectTests
{
    private readonly AccountingFixture _fixture;

    public CapitalProjectTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cwip_collects_cost_and_clears_on_capitalisation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.Accounts.Add(new LedgerAccount(companyId, "1695", "Capital Work in Progress", AccountType.Asset));
        await db.SaveChangesAsync();

        ICapitalProjectService cwip = scope.ServiceProvider.GetRequiredService<ICapitalProjectService>();
        (await cwip.OpenAsync(companyId, "wh-b", "Warehouse B", "4000", 10_000m, new DateOnly(2026, 1, 10))).Error.Code.Should().Be("Fa.CwipAccount");
        long project = (await cwip.OpenAsync(companyId, "wh-b", "Warehouse B", "1695", 10_000m, new DateOnly(2026, 1, 10))).Value;
        (await cwip.AddCostAsync(companyId, project, new DateOnly(2026, 1, 5), 100m, "1100", "Survey")).Error.Code.Should().Be("Fa.CostBeforeOpen");
        var foundations = await cwip.AddCostAsync(companyId, project, new DateOnly(2026, 1, 20), 4_000m, "1100", "Foundations");
        foundations.IsSuccess.Should().BeTrue(foundations.IsFailure ? foundations.Error.Message : null);
        (await cwip.AddCostAsync(companyId, project, new DateOnly(2026, 2, 15), 3_000m, "1100", "Roof")).IsSuccess.Should().BeTrue();

        RegisterAssetRequest asset = new()
        {
            CompanyId = companyId, AssetNumber = "WH-B", Description = "Warehouse B", AssetClass = "Buildings", AcquisitionDate = new DateOnly(2026, 3, 1), Method = DepreciationMethod.StraightLine,
            UsefulLifeMonths = 240, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
        };
        (await cwip.CapitaliseAsync(project, new RegisterAssetRequest
        {
            CompanyId = companyId, AssetNumber = "WH-B", Description = "Warehouse B", AssetClass = "Buildings", AcquisitionDate = new DateOnly(2026, 2, 1), Method = DepreciationMethod.StraightLine,
            UsefulLifeMonths = 240, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
        })).Error.Code.Should().Be("Fa.CapitaliseBeforeCost");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(DateOnly from, DateOnly to)
        {
            var rendered = await runner.RenderAsync(companyId, "FA211", new Dictionary<string, object?> { ["FromDate"] = from, ["ToDate"] = to }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))).Should().Contain("WH-B,Warehouse B,2026-01-10,In progress,\"10,000.00\",\"4,000.00\",\"3,000.00\",0.00,70.0%,7000");

        long assetId = (await cwip.CapitaliseAsync(project, asset)).Value;
        (await cwip.CapitaliseAsync(project, asset)).Error.Code.Should().Be("Fa.ProjectCapitalised");
        (await cwip.AddCostAsync(companyId, project, new DateOnly(2026, 3, 5), 50m, "1100", "Snag")).Error.Code.Should().Be("Fa.ProjectCapitalised");
        (await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == assetId)).Cost.Should().Be(7_000m);
        decimal cwipBalance = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "1695").SumAsync(l => l.BaseDebit - l.BaseCredit);
        cwipBalance.Should().Be(0m);

        (await Render(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Should().Contain("WH-B,Warehouse B,2026-01-10,Capitalised WH-B,\"10,000.00\",\"7,000.00\",0.00,\"7,000.00\",70.0%,0");
    }
}
