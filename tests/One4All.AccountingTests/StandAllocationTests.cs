using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers stand allocation: three applicants and two available stands in a scheme. A seniority run pairs the two
/// longest-waiting; a second run cannot take the same stands while the first is live; the proposer cannot approve;
/// approval issues certificates and offers and marks the stands allocated; one offer is accepted, the other declined,
/// returning its stand; a seeded ballot over the rest verifies from its stored seed and inputs and reproduces exactly
/// after cancellation; a lapsed offer returns its stand; the database refuses a second live allocation of a stand.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandAllocationTests
{
    private readonly AccountingFixture _fixture;

    public StandAllocationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stands_are_allocated_under_control_and_ballots_reverify()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-AL-{companyId}", "Allocation parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-AL-{companyId}", "Allocation Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, scheme.Id, "AL-1", 300m, 5000m);
        HousingStand second = new(companyId, parcel.Id, scheme.Id, "AL-2", 300m, 5000m);
        db.HousingStands.AddRange(first, second);
        await db.SaveChangesAsync();

        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        async Task<string> Register(string id, int year) => (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, new WaitingListApplication
        {
            CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
            ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
        })).Value.WaitingListNumber;
        string oldest = await Register("AL-OLD", 2015);
        string middle = await Register("AL-MID", 2018);
        string newest = await Register("AL-NEW", 2022);

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationProposal Proposal(AllocationMethod method, long? seed = null) => new()
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = method, Seed = seed, AsAtDate = new DateOnly(2026, 5, 1), UserId = 71,
        };

        AllocationRunView seniority = (await allocations.ProposeAsync(Proposal(AllocationMethod.Seniority))).Value;
        seniority.Lines.Select(l => (l.StandNumber, l.WaitingListNumber)).Should().Equal(("AL-1", oldest), ("AL-2", middle));
        (await allocations.ProposeAsync(Proposal(AllocationMethod.Seniority))).Error.Code.Should().Be("Hse.NothingToAllocate");
        (await allocations.VerifyDrawAsync(companyId, seniority.RunNumber)).Value.IsVerified.Should().BeTrue();

        (await allocations.ApproveAsync(companyId, seniority.RunNumber, 71, new DateOnly(2026, 5, 2), 14)).Error.Code.Should().Be("Hse.SelfApproval");
        AllocationRunView approved = (await allocations.ApproveAsync(companyId, seniority.RunNumber, 72, new DateOnly(2026, 5, 2), 14)).Value;
        approved.Lines.Should().OnlyContain(l => l.Status == AllocationStatus.Offered && l.CertificateNumber!.StartsWith("AC-"));
        (await db.HousingStands.AsNoTracking().Where(s => s.SchemeId == scheme.Id).Select(s => s.Status).ToListAsync()).Should().OnlyContain(s => s == StandStatus.Allocated);

        string acceptCert = approved.Lines[0].CertificateNumber!;
        string declineCert = approved.Lines[1].CertificateNumber!;
        (await allocations.AcceptAsync(companyId, acceptCert, new DateOnly(2026, 5, 20))).Error.Code.Should().Be("Hse.OfferExpired");
        (await allocations.AcceptAsync(companyId, acceptCert, new DateOnly(2026, 5, 10))).IsSuccess.Should().BeTrue();
        (await allocations.AcceptAsync(companyId, acceptCert, new DateOnly(2026, 5, 10))).Error.Code.Should().Be("Hse.OfferNotOpen");
        (await allocations.DeclineAsync(companyId, declineCert, new DateOnly(2026, 5, 11), "Relocating")).IsSuccess.Should().BeTrue();
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == second.Id)).Status.Should().Be(StandStatus.Available);
        (await list.EnquireAsync(companyId, oldest, new DateOnly(2026, 5, 12))).Value.Status.Should().Be(ApplicantStatus.Allocated);

        // A seeded ballot for the freed stand, over the two still waiting; cancelled and re-run with the same seed.
        AllocationRunView ballot = (await allocations.ProposeAsync(Proposal(AllocationMethod.Ballot, seed: 2026))).Value;
        ballot.Seed.Should().Be(2026);
        ballot.Lines.Should().ContainSingle().Which.StandNumber.Should().Be("AL-2");
        (await allocations.VerifyDrawAsync(companyId, ballot.RunNumber)).Value.IsVerified.Should().BeTrue();
        (await allocations.CancelAsync(companyId, ballot.RunNumber)).IsSuccess.Should().BeTrue();
        AllocationRunView rerun = (await allocations.ProposeAsync(Proposal(AllocationMethod.Ballot, seed: 2026))).Value;
        rerun.InputHash.Should().Be(ballot.InputHash);
        rerun.Lines.Select(l => l.WaitingListNumber).Should().Equal(ballot.Lines.Select(l => l.WaitingListNumber));
        new[] { middle, newest }.Should().Contain(rerun.Lines[0].WaitingListNumber);

        // Offered and left to lapse: the stand returns to the pool.
        AllocationRunView offered = (await allocations.ApproveAsync(companyId, rerun.RunNumber, 72, new DateOnly(2026, 6, 1), 7)).Value;
        string lateCert = offered.Lines[0].CertificateNumber!;
        (await allocations.ExpiredOffersAsync(companyId, new DateOnly(2026, 6, 8))).Should().BeEmpty();
        (await allocations.ExpiredOffersAsync(companyId, new DateOnly(2026, 6, 9))).Should().ContainSingle().Which.Should().Match<ExpiredOffer>(o => o.CertificateNumber == lateCert && o.DaysOverdue == 1);
        (await allocations.ExtendOfferAsync(companyId, lateCert, new DateOnly(2026, 6, 5), "Too early")).Error.Code.Should().Be("Hse.OfferExtension");
        (await allocations.ExtendOfferAsync(companyId, lateCert, new DateOnly(2026, 6, 20), "Deposit on its way")).IsSuccess.Should().BeTrue();
        (await allocations.ExpiredOffersAsync(companyId, new DateOnly(2026, 6, 9))).Should().BeEmpty("the extended offer is open again");
        (await allocations.LapseExpiredAsync(companyId, new DateOnly(2026, 6, 9))).Should().BeEmpty();
        (await allocations.LapseOfferAsync(companyId, lateCert, new DateOnly(2026, 6, 21), " ")).Error.Code.Should().Be("Hse.ReasonRequired");
        (await allocations.LapseOfferAsync(companyId, lateCert, new DateOnly(2026, 6, 21), "No response after the extension")).IsSuccess.Should().BeTrue();
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == second.Id)).Status.Should().Be(StandStatus.Available);

        // The database itself refuses a second live allocation of the accepted stand.
        AllocationRun rogue = new(companyId, $"ROGUE-{companyId}", scheme.Id, HousingCategory.HighDensity, AllocationMethod.Seniority, null, string.Empty, string.Empty, string.Empty, 71);
        HousingApplicant someone = await db.HousingApplicants.SingleAsync(a => a.CompanyId == companyId && a.WaitingListNumber == newest);
        rogue.Add(new StandAllocation(1, first.Id, someone.Id));
        db.Set<AllocationRun>().Add(rogue);
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
