using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the allocation committee acting on individual pairings (brief §4.10.4): a pairing rejected with a reason leaves its stand available and the
/// applicant recorded as rejected; a substitution puts the next eligible applicant on the pairing and records why the skipped one was skipped; the
/// approval then offers only what stands, and keeps the meeting's minute reference, date and members.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllocationCommitteeDecisionTests
{
    private readonly AccountingFixture _fixture;

    public AllocationCommitteeDecisionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_committee_rejects_and_substitutes_pairings_before_approving()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        HousingScheme scheme = new(companyId, $"CD-{companyId}", "Committee decisions", SchemeType.CouncilDelivered);
        LandParcel parcel = new(companyId, $"CD-P-{companyId}", "Layout", 900m, 9000m);
        db.AddRange(scheme, parcel);
        await db.SaveChangesAsync();
        db.HousingStands.AddRange(Enumerable.Range(1, 3).Select(i => new HousingStand(companyId, parcel.Id, scheme.Id, $"CD-{companyId}-{i}", 300m, 3_000m, new DateOnly(2026, 1, 5))));
        foreach ((string id, int year) in new[] { ("A", 2010), ("B", 2011), ("C", 2012), ("D", 2013) })
        {
            HousingApplicant applicant = new(companyId, $"CD-{id}-{companyId}", id, new DateOnly(year, 1, 1));
            applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1980, 1, 1), new DateOnly(2000, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
            applicant.Register($"WL-CD-{id}-{companyId}", applicant.RegistrationDate);
            db.HousingApplicants.Add(applicant);
        }

        await db.SaveChangesAsync();
        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        run.Lines.Select(l => l.WaitingListNumber).Should().Equal($"WL-CD-A-{companyId}", $"WL-CD-B-{companyId}", $"WL-CD-C-{companyId}");

        (await allocations.RejectLineAsync(companyId, run.RunNumber, 2, " ")).Error.Code.Should().Be("Hse.ReasonRequired");
        (await allocations.RejectLineAsync(companyId, run.RunNumber, 9, "No such line")).Error.Code.Should().Be("Hse.LineNotProposed");
        AllocationRunView rejected = (await allocations.RejectLineAsync(companyId, run.RunNumber, 2, "Already housed by the employer")).Value;
        rejected.Lines.Single(l => l.Sequence == 2).Status.Should().Be(AllocationStatus.Cancelled);
        AllocationRunView substituted = (await allocations.SubstituteLineAsync(companyId, run.RunNumber, 3, "Documents expired")).Value;
        substituted.Lines.Single(l => l.Sequence == 3).WaitingListNumber.Should().Be($"WL-CD-D-{companyId}", "D is next on the list");
        (await allocations.SubstituteLineAsync(companyId, run.RunNumber, 1, "Nobody left after D")).Error.Code.Should().Be("Hse.NoSubstitute");

        AllocationRunView approved = (await allocations.ApproveByCommitteeAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30,
            new CommitteeMeeting("ALLOC-COM 3/2026", new DateOnly(2026, 3, 1), "T. Moyo (chair), R. Ncube, C. Dube"))).Value;
        approved.Lines.Where(l => l.Status == AllocationStatus.Offered).Select(l => l.WaitingListNumber).Should().Equal($"WL-CD-A-{companyId}", $"WL-CD-D-{companyId}");
        approved.Lines.Single(l => l.Sequence == 2).Status.Should().Be(AllocationStatus.Cancelled);
        AllocationRun kept = await db.Set<AllocationRun>().AsNoTracking().Include(r => r.Exclusions).SingleAsync(r => r.CompanyId == companyId && r.RunNumber == run.RunNumber);
        (kept.CommitteeResolution, kept.MeetingDate, kept.CommitteeMembers).Should().Be(("ALLOC-COM 3/2026", new DateOnly(2026, 3, 1), "T. Moyo (chair), R. Ncube, C. Dube"));
        kept.Exclusions.Select(e => (e.Reason, e.Note)).Should().Contain((ExclusionReason.CommitteeRejected, "Already housed by the employer")).And.Contain((ExclusionReason.CommitteeSkipped, "Documents expired"));
        Dictionary<string, StandStatus> stands = await db.HousingStands.AsNoTracking().Where(s => s.SchemeId == scheme.Id).ToDictionaryAsync(s => s.StandNumber, s => s.Status);
        (stands[$"CD-{companyId}-1"], stands[$"CD-{companyId}-2"], stands[$"CD-{companyId}-3"]).Should().Be((StandStatus.Allocated, StandStatus.Available, StandStatus.Allocated));
        (await allocations.RejectLineAsync(companyId, run.RunNumber, 1, "Too late")).Error.Code.Should().Be("Hse.RunNotProposed");
    }
}
