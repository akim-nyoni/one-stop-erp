using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers allocation fairness and control (brief §14 H34, H37, H91, H93): a run proposed over a scheme where one stand is
/// under a land dispute leaves that stand out (so approval succeeds) and records the suspended applicant it left out; one
/// offer is declined; quotas are compared with the groups of those allocated; the approval audit and override report
/// show who approved the run and the offers made without an approved price.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllocationControlReportsTests
{
    private readonly AccountingFixture _fixture;

    public AllocationControlReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Exclusions_quotas_and_approvals_are_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        HousingScheme scheme = new(companyId, $"SCH-AC-{companyId}", "Control Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        string parcel = $"LP-AC-{companyId}";
        long parcelId = (await housing.AcquireLandAsync(companyId, parcel, "Layout", 900m, 9000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-AC")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("AC-1", 300m), new NewStand("AC-2", 300m), new NewStand("AC-3", 300m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ILandRegisterService>().OpenDisputeAsync(new LandDisputeLodgement
        {
            CompanyId = companyId, ParcelNumber = parcel, StandNumber = "AC-3", Type = LandDisputeType.Encroachment, Claimant = "Neighbour", Particulars = "Shed over line", Date = new DateOnly(2026, 2, 1),
        })).IsSuccess.Should().BeTrue();

        // Youth applicant first in seniority, a suspended applicant, a woman, and one more.
        foreach ((string id, int year, DateOnly born) in new[]
        {
            ($"AC-A-{companyId}", 2010, new DateOnly(1998, 1, 1)), ($"AC-S-{companyId}", 2011, new DateOnly(1970, 1, 1)),
            ($"AC-W-{companyId}", 2012, new DateOnly(1975, 1, 1)), ($"AC-D-{companyId}", 2013, new DateOnly(1980, 1, 1)),
        })
        {
            HousingApplicant applicant = new(companyId, id, id, new DateOnly(year, 1, 1));
            applicant.SetDetails(HousingCategory.HighDensity, born, new DateOnly(2000, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
            applicant.Register($"WL-{id}", applicant.RegistrationDate);
            db.HousingApplicants.Add(applicant);
        }

        await db.SaveChangesAsync();
        db.HousingApplicants.Single(a => a.IdNumber == $"AC-S-{companyId}").SetStatus(ApplicantStatus.Suspended);
        await db.SaveChangesAsync();

        IAllocationQuotaService quotas = scope.ServiceProvider.GetRequiredService<IAllocationQuotaService>();
        (await quotas.DeclareGroupsAsync(companyId, $"WL-AC-W-{companyId}", BeneficiaryGroups.Women)).IsSuccess.Should().BeTrue();
        (await quotas.SetQuotaAsync(companyId, null, BeneficiaryGroups.Women, 30m)).IsSuccess.Should().BeTrue();
        (await quotas.SetQuotaAsync(companyId, null, BeneficiaryGroups.Youth, 60m)).IsSuccess.Should().BeTrue();
        (await quotas.SetQuotaAsync(companyId, scheme.Code, BeneficiaryGroups.Youth, 50m)).IsSuccess.Should().BeTrue();
        (await quotas.SetQuotaAsync(companyId, null, BeneficiaryGroups.Women | BeneficiaryGroups.Youth, 10m)).Error.Code.Should().Be("Hse.QuotaDetails");

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        run.Lines.Select(l => l.StandNumber).Should().Equal("AC-1", "AC-2"); // the disputed stand is left out
        run.Lines.Select(l => l.WaitingListNumber).Should().Equal($"WL-AC-A-{companyId}", $"WL-AC-W-{companyId}");
        AllocationRunView approved = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30)).Value;
        (await allocations.DeclineAsync(companyId, approved.Lines[1].CertificateNumber!, new DateOnly(2026, 3, 10), "Prefers another area")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string skipped = await Render("H34");
        skipped.Should().Contain($"{run.RunNumber},{scheme.Code},Seniority,WL-AC-S-{companyId},AC-S-{companyId},Left out,Suspended for not confirming on the list")
            .And.Contain($"WL-AC-W-{companyId},AC-W-{companyId},Declined,Prefers another area");

        // One allocation stands (the youth applicant's); the scheme's own youth quota overrides the council-wide one.
        string quota = await Render("H37");
        quota.Should().Contain($"{scheme.Code},Youth,1,1,100.0,50.0,+50.0,Met").And.Contain($"{scheme.Code},Women,1,0,0.0,30.0,-30.0,Short");

        (await Render("H91")).Should().Contain($"Allocation run,{run.RunNumber},user 71,user 72,").And.Contain(",2026-03-02,");
        (await Render("H93")).Should().Contain("Offer without an approved price").And.Contain(approved.Lines[0].CertificateNumber!);
    }
}
