using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the first H-series housing reports (brief §14 I-B): each renders through the generic runner against live data —
/// the land bank and acquisitions with what blocks them, the waiting list in seniority order and its age analysis, the
/// allocation register and offers with their countdown, and the double-allocation exception report, which is empty until
/// a stand is promised twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Housing_reports_render_live_registers_and_exceptions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        LandParcel parcel = new(companyId, $"LP-HR-{companyId}", "Report parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-HR-{companyId}", "Report Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, scheme.Id, "HR-1", 300m, 1000m);
        HousingStand second = new(companyId, parcel.Id, scheme.Id, "HR-2", 300m, 1000m);
        db.HousingStands.AddRange(first, second);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<ILandAcquisitionService>().OpenAsync(new AcquisitionCase
        {
            CompanyId = companyId, ParcelNumber = $"LP-HR2-{companyId}", Description = "Farm 9", AreaSquareMetres = 10000m, Method = AcquisitionMethod.Purchase,
            Counterparty = "Seller", ResolutionReference = "Res 1/2026", Date = new DateOnly(2026, 1, 5),
        });

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        foreach ((string id, int year) in new[] { ("HR-A", 2012), ("HR-B", 2020), ("HR-C", 2025) })
        {
            (await list.RegisterAsync(new WaitingListApplication
            {
                CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
                ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 6, 1),
            })).IsSuccess.Should().BeTrue();
        }

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        Dictionary<string, object?> asAt = new() { ["AsAtDate"] = new DateOnly(2026, 3, 25) };
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, asAt, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        foreach (string code in HousingReports.All().Select(r => r.Code))
        {
            await Render(code);
        }

        (await Render("H1")).Should().Contain(parcel.ParcelNumber);
        (await Render("H4")).Should().Contain($"LP-HR2-{companyId}").And.Contain("No valuation obtained");
        string waiting = await Render("H18");
        waiting.IndexOf("HR-A", StringComparison.Ordinal).Should().BeLessThan(waiting.IndexOf("HR-B", StringComparison.Ordinal));
        (await Render("H20")).Should().Contain("HighDensity");
        string register = await Render("H31");
        register.Should().Contain("HR-1").And.Contain("HR-2").And.Contain("Offered");
        (await Render("H35")).Should().Contain("Expiring"); // 25 March, offers expire 31 March

        // A stand promised twice — offered to an applicant and given to a cooperative behind the system's back.
        string clean = await Render("H39");
        clean.Should().NotContain("HR-1");
        db.Set<Cooperative>().Add(new Cooperative(companyId, $"HRC-{companyId}", "Rogue Coop", "R-1"));
        await db.SaveChangesAsync();
        long coop = db.Set<Cooperative>().Single(c => c.CompanyId == companyId).Id;
        db.Set<CooperativeStand>().Add(new CooperativeStand(companyId, coop, first.Id, new DateOnly(2026, 3, 10)));
        await db.SaveChangesAsync();
        (await Render("H39")).Should().Contain("HR-1").And.Contain("cooperative").And.Contain("allocation");
    }
}
