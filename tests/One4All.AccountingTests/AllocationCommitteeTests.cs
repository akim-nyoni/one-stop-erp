using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the allocation committee, swaps and substitutions: a list approved on the committee's resolution, two allottees swapping stands
/// on a swap document, and a declined stand offered to the next on the list in a substitution run.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllocationCommitteeTests
{
    private readonly AccountingFixture _fixture;

    public AllocationCommitteeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_committee_approves_allottees_swap_and_declined_stands_are_substituted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        HousingScheme scheme = new(companyId, $"SCH-AC2-{companyId}", "Committee Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-AC2-{companyId}", "Layout", 900m, 9000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-AC2")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("AC2-1", 300m), new NewStand("AC2-2", 300m)], ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460"))
            .IsSuccess.Should().BeTrue();
        foreach ((string id, int year) in new[] { ("A", 2010), ("B", 2011), ("C", 2012) })
        {
            HousingApplicant applicant = new(companyId, $"AC2-{id}-{companyId}", id, new DateOnly(year, 1, 1));
            applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1980, 1, 1), new DateOnly(2000, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
            applicant.Register($"WL-AC2-{id}-{companyId}", applicant.RegistrationDate);
            db.HousingApplicants.Add(applicant);
        }

        await db.SaveChangesAsync();
        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationProposal Proposal(params string[] stands) => new()
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71, StandNumbers = stands,
        };
        AllocationRunView run = (await allocations.ProposeAsync(Proposal())).Value;
        run.Lines.Select(l => (l.StandNumber, l.WaitingListNumber)).Should().Equal(("AC2-1", $"WL-AC2-A-{companyId}"), ("AC2-2", $"WL-AC2-B-{companyId}"));
        (await allocations.ApproveByCommitteeAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30, " ")).Error.Code.Should().Be("Hse.CommitteeResolution");
        AllocationRunView approved = (await allocations.ApproveByCommitteeAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30, "ALLOC-COM 3/2026")).Value;
        (await db.Set<AllocationRun>().AsNoTracking().SingleAsync(r => r.CompanyId == companyId && r.RunNumber == run.RunNumber)).CommitteeResolution.Should().Be("ALLOC-COM 3/2026");

        // A and B swap stands on a signed swap request.
        string a = approved.Lines[0].CertificateNumber!, b = approved.Lines[1].CertificateNumber!;
        (await allocations.SwapAsync(companyId, a, a, new DateOnly(2026, 3, 5), "SW-1", "Closer to work", 72)).Error.Code.Should().Be("Hse.SwapOffers");
        long swap = (await allocations.SwapAsync(companyId, a, b, new DateOnly(2026, 3, 5), "SW-1", "Closer to work", 72)).Value;
        (await allocations.SwapAsync(companyId, b, a, new DateOnly(2026, 3, 5), "SW-2", "Again", 72)).Error.Code.Should().Be("Hse.SwapPending");
        (await db.Set<StandAllocation>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.CertificateNumber == a)).StandId
            .Should().Be(await db.HousingStands.AsNoTracking().Where(s => s.SchemeId == scheme.Id && s.StandNumber == "AC2-1").Select(s => s.Id).SingleAsync(), "nothing moves before approval");
        (await allocations.DecideSwapAsync(companyId, swap, true, 72, new DateOnly(2026, 3, 5), null)).Error.Code.Should().Be("Hse.SwapSelfApproval");
        (await allocations.DecideSwapAsync(companyId, swap, false, 73, new DateOnly(2026, 3, 5), " ")).Error.Code.Should().Be("Hse.SwapReason");
        (await allocations.DecideSwapAsync(companyId, swap, true, 73, new DateOnly(2026, 3, 5), null)).IsSuccess.Should().BeTrue();
        StandSwap approvedSwap = await db.Set<StandSwap>().AsNoTracking().SingleAsync(s => s.Id == swap);
        (approvedSwap.Status, approvedSwap.RequestedByUserId, approvedSwap.ApprovedByUserId).Should().Be((StandSwapStatus.Approved, 72L, (long?)73));
        Dictionary<string, long> standOf = await db.Set<StandAllocation>().AsNoTracking().Where(x => x.CompanyId == companyId && (x.CertificateNumber == a || x.CertificateNumber == b))
            .ToDictionaryAsync(x => x.CertificateNumber!, x => x.StandId);
        Dictionary<string, long> stands = await db.HousingStands.AsNoTracking().Where(s => s.SchemeId == scheme.Id).ToDictionaryAsync(s => s.StandNumber, s => s.Id);
        (standOf[a], standOf[b]).Should().Be((stands["AC2-2"], stands["AC2-1"]));

        // B declines; the stand goes to C, next on the list, in a substitution run for that stand alone.
        (await allocations.DeclineAsync(companyId, b, new DateOnly(2026, 3, 6), "Moved away")).IsSuccess.Should().BeTrue();
        AllocationRunView substitution = (await allocations.ProposeAsync(Proposal("AC2-1"))).Value;
        substitution.Lines.Should().ContainSingle().Which.Should().Match<AllocationLine>(l => l.StandNumber == "AC2-1" && l.WaitingListNumber == $"WL-AC2-C-{companyId}");
    }
}
