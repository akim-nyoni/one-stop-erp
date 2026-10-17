using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the allocation and waiting-list depth reports (H3–H42): a ballot run is listed as proposed then re-verified;
/// score derivations list each factor; an applicant who holds a council stand is flagged; a declined offer is reported with
/// its reason; accepted allocations without an agreement are aged; and a person holding one stand and allocated another
/// appears among beneficiaries with more than one stand.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingAllocationReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingAllocationReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Allocation_and_waiting_list_reports_reflect_the_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string ownerId = $"HA-A-{companyId}";
        Customer owner = new(companyId, "HA-OWN", "Applicant A", Scenario.Usd) { NationalIdNumber = ownerId };
        db.Customers.Add(owner);
        LandParcel parcel = new(companyId, $"LP-HA-{companyId}", "Allocation parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-HA-{companyId}", "Allocation Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand held = new(companyId, parcel.Id, null, "HA-0", 300m, 1000m);
        db.HousingStands.Add(held);
        db.HousingStands.AddRange(Enumerable.Range(1, 3).Select(i => new HousingStand(companyId, parcel.Id, scheme.Id, $"HA-{i}", 300m, 1000m)));
        await db.SaveChangesAsync();
        held.Sell(owner.Id, owner.Name, 3000m, new DateOnly(2025, 6, 1));
        await db.SaveChangesAsync();

        // Applicant A is registered first (before owning anything), then B and C.
        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        foreach ((string id, int year) in new[] { (ownerId, 2014), ($"HA-B-{companyId}", 2016), ($"HA-C-{companyId}", 2018) })
        {
            db.HousingApplicants.Add(new HousingApplicant(companyId, id, id, new DateOnly(year, 1, 1)));
        }

        await db.SaveChangesAsync();
        foreach (HousingApplicant applicant in db.HousingApplicants.Where(a => a.CompanyId == companyId).ToList())
        {
            applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1985, 1, 1), new DateOnly(2000, 1, 1), 2, false, 400m, CurrentTenure.Lodger);
            applicant.Register($"WL-{applicant.IdNumber}", applicant.RegistrationDate);
        }

        await db.SaveChangesAsync();

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Ballot, Seed = 42, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("H30")).Should().Contain(run.RunNumber).And.Contain("Ballot");
        (await Render("H22")).Should().Contain("Housing conditions").And.Contain($"WL-HA-B-{companyId}");
        (await Render("H19")).Should().Contain("300 – 599").And.Contain("36 – 50");
        (await Render("H24")).Should().Contain($"WL-{ownerId}").And.Contain("council stand HA-0").And.NotContain($"WL-HA-B-{companyId}");

        AllocationRunView offered = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).Value;
        (await Render("H32")).Should().Contain(run.RunNumber).And.Contain("42").And.Contain("Inputs and draw reproduce");
        (await Render("H33")).Should().Contain("2026-03").And.Contain("72");

        // Applicant A already holds HA-0, so one beneficiary, one stand keeps A out of the run (brief §4.10.4); B and C are offered the stands.
        offered.Lines.Should().HaveCount(2).And.NotContain(l => l.WaitingListNumber == $"WL-{ownerId}");
        (await Render("H34")).Should().Contain("Already holds a stand");
        AllocationLine forOwner = offered.Lines.Single(l => l.WaitingListNumber == $"WL-HA-B-{companyId}");
        AllocationLine declined = offered.Lines.First(l => l != forOwner);
        (await allocations.AcceptAsync(companyId, forOwner.CertificateNumber!, new DateOnly(2026, 3, 10))).IsSuccess.Should().BeTrue();
        (await allocations.DeclineAsync(companyId, declined.CertificateNumber!, new DateOnly(2026, 3, 12), "Prefers another area")).IsSuccess.Should().BeTrue();

        (await Render("H36")).Should().Contain(declined.CertificateNumber).And.Contain("Declined").And.Contain("Prefers another area");
        (await Render("H41")).Should().Contain(forOwner.CertificateNumber).And.Contain("51"); // accepted 10 March, 51 days by 30 April
        // A second stand sold to A's customer account puts A on the multiple-stands report.
        HousingStand second = new(companyId, parcel.Id, null, "HA-9", 300m, 1000m);
        db.HousingStands.Add(second);
        await db.SaveChangesAsync();
        second.Sell(owner.Id, owner.Name, 3000m, new DateOnly(2026, 4, 1));
        await db.SaveChangesAsync();
        (await Render("H40")).Should().Contain(ownerId).And.Contain("HA-0 holds").And.Contain("HA-9 holds");
        (await Render("H11")).Should().Contain(parcel.ParcelNumber).And.Contain("HA-3");
    }
}
