using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers asset verification, insurance / warranty and planned maintenance (brief §14 M 210, 213, 214): a count finds one asset in
/// place, one elsewhere (moved on close) and an unregistered tag, and reports the unseen one missing; lapsing cover, under-insurance and
/// expired warranties are flagged; a planned service logged as done rolls its next due date.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssetControlTests
{
    private readonly AccountingFixture _fixture;

    public AssetControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Counts_cover_and_maintenance_are_controlled_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        IFixedAssetService fa = scope.ServiceProvider.GetRequiredService<IFixedAssetService>();
        async Task<long> Register(string number, string description, decimal cost, string location)
        {
            long id = (await fa.RegisterAsync(new RegisterAssetRequest
            {
                CompanyId = companyId, AssetNumber = number, Description = description, AssetClass = "Equipment", Cost = cost, AcquisitionDate = new DateOnly(2026, 1, 1),
                Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 36, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6000",
                FundingAccountCode = "1100", DocumentNumber = $"FA-{number}",
            })).Value;
            (await fa.TransferAsync(companyId, id, location, null)).IsSuccess.Should().BeTrue();
            return id;
        }

        long laptop = await Register("ACL-LAP", "Laptop", 1_200m, "HQ");
        long printer = await Register("ACL-PRN", "Printer", 600m, "HQ");
        long forklift = await Register("ACL-FRK", "Forklift", 9_000m, "Yard");
        FixedAsset laptopRow = await db.FixedAssets.SingleAsync(a => a.Id == laptop);
        (laptopRow.Insurer, laptopRow.PolicyNumber, laptopRow.InsuredValue, laptopRow.InsuranceExpiry) = ("Safe Ins", "P-1", 1_000m, new DateOnly(2026, 4, 10));
        (await db.FixedAssets.SingleAsync(a => a.Id == printer)).WarrantyExpiry = new DateOnly(2026, 3, 1);
        FixedAsset forkliftRow = await db.FixedAssets.SingleAsync(a => a.Id == forklift);
        (forkliftRow.InsuredValue, forkliftRow.InsuranceExpiry, forkliftRow.WarrantyExpiry) = (10_000m, new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1));
        forkliftRow.TagNumber = "BC-0099";
        await db.SaveChangesAsync();

        IAssetControlService control = scope.ServiceProvider.GetRequiredService<IAssetControlService>();
        long count = (await control.StartCountAsync(companyId, new DateOnly(2026, 3, 15), "HQ")).Value;
        (await control.RecordSightingAsync(companyId, count, "acl-lap", "HQ", "Good")).IsSuccess.Should().BeTrue();
        (await control.RecordSightingAsync(companyId, count, "bc-0099", "HQ", "Worn tyres")).IsSuccess.Should().BeTrue(); // the forklift's barcode
        (await control.RecordSightingAsync(companyId, count, "GHOST-1", "HQ", null)).IsSuccess.Should().BeTrue();
        (await control.CloseCountAsync(companyId, count, updateLocations: true)).Value.Should().Be(1);
        (await control.RecordSightingAsync(companyId, count, "ACL-PRN", "HQ", null)).Error.Code.Should().Be("Fa.CountClosed");
        (await db.FixedAssets.AsNoTracking().SingleAsync(a => a.Id == forklift)).Location.Should().Be("HQ");

        long service = (await control.PlanMaintenanceAsync(companyId, forklift, "Service", 6, new DateOnly(2026, 3, 1))).Value;
        (await control.PlanMaintenanceAsync(companyId, laptop, "Clean", 12, new DateOnly(2026, 4, 15))).IsSuccess.Should().BeTrue();
        (await control.PlanMaintenanceAsync(companyId, laptop, "Clean", 0, new DateOnly(2026, 4, 15))).Error.Code.Should().Be("Fa.PlanInvalid");
        (await control.CompletePlannedAsync(companyId, service, new DateOnly(2026, 3, 20), 250m, "Fix Co")).IsSuccess.Should().BeTrue();
        (await db.MaintenancePlans.AsNoTracking().SingleAsync(p => p.Id == service)).NextDue.Should().Be(new DateOnly(2026, 9, 20));

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31), ["Days"] = 30 };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string countNumber = (await db.AssetCounts.AsNoTracking().SingleAsync(c => c.Id == count)).CountNumber;
        (await Render("FA210")).Should().Contain($"{countNumber},2026-03-15,ACL-PRN,Printer,HQ,—,,Missing,600").And.Contain("ACL-FRK,Forklift,Yard,HQ,Worn tyres,Found elsewhere,0")
            .And.Contain("GHOST-1,Not on the register,—,HQ,,Unregistered,0").And.Contain("ACL-LAP,Laptop,HQ,HQ,Good,Found,0");
        string cover = await Render("FA213");
        cover.Should().Contain("ACL-LAP,Laptop,Safe Ins P-1,2026-04-10,—,Insurance ends in 10 days; Under-insured,\"1,000.00\",1200")
            .And.Contain("ACL-PRN,Printer,—,—,2026-03-01,Not insured; Warranty expired,—,600").And.NotContain("ACL-FRK");
        (await Render("FA214")).Should().Contain("Schedule,ACL-LAP,Laptop,Clean (every 12 months),—,2026-04-15,Due soon,0")
            .And.Contain("Schedule,ACL-FRK,Forklift,Service (every 6 months),2026-03-20,2026-09-20,Planned,0").And.Contain("History,ACL-FRK,Forklift,Service (planned),2026-03-20,Fix Co,Done,250");
    }
}
