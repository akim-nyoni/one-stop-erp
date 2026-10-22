using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the appeals register: a one-stand run allocates the longest-waiting applicant; the allocated applicant cannot
/// appeal, the other can (once); an appeal is not decided before its hearing; upheld with reasons; a certificate not
/// issued to the appellant cannot be linked, while the certificate from a later run that allocated them can; the
/// register shows it all.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AllocationAppealTests
{
    private readonly AccountingFixture _fixture;

    public AllocationAppealTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Appeals_are_heard_decided_and_linked_to_reallocations()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-AP-{companyId}", "Appeal parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-AP-{companyId}", "Appeal Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand first = new(companyId, parcel.Id, scheme.Id, "AP-1", 300m, 5000m);
        db.HousingStands.Add(first);
        await db.SaveChangesAsync();

        async Task<string> Register(string id, int year) => (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, new WaitingListApplication
        {
            CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
            ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
        })).Value.WaitingListNumber;
        string winner = await Register("AP-WIN", 2015);
        string appellant = await Register("AP-APP", 2016);

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationProposal Proposal() => new() { CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 5, 1), UserId = 71 };
        string run = (await allocations.ProposeAsync(Proposal())).Value.RunNumber;

        // A stand promised elsewhere since the run was proposed cannot be allocated again: the approval is refused and the attempt is logged for H39.
        HousingStand stand = await db.HousingStands.SingleAsync(s => s.Id == first.Id);
        stand.MarkAllocated();
        await db.SaveChangesAsync();
        (await allocations.ApproveAsync(companyId, run, 72, new DateOnly(2026, 5, 2), 30)).Error.Code.Should().Be("Hse.StandUnavailable");
        (await db.AllocationBlockLogs.AsNoTracking().Where(b => b.CompanyId == companyId).ToListAsync()).Should().ContainSingle(b => b.StandNumber == "AP-1" && b.RunNumber == run && b.AttemptedByUserId == 72);
        stand = await db.HousingStands.SingleAsync(s => s.Id == first.Id); // the refusal's logging cleared the context's tracking
        stand.ReleaseAllocation();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var approved = await allocations.ApproveAsync(companyId, run, 72, new DateOnly(2026, 5, 2), 30);
        approved.IsSuccess.Should().BeTrue(approved.IsFailure ? approved.Error.Code + ": " + approved.Error.Message : string.Empty);
        string winnerCert = approved.Value.Lines[0].CertificateNumber!;

        IAllocationAppealService appeals = scope.ServiceProvider.GetRequiredService<IAllocationAppealService>();
        (await appeals.LodgeAsync(companyId, winner, run, "Wanted another stand", new DateOnly(2026, 5, 5))).Error.Code.Should().Be("Hse.NotUnsuccessful");
        string appeal = (await appeals.LodgeAsync(companyId, appellant, run, "Disability not scored", new DateOnly(2026, 5, 5))).Value;
        (await appeals.LodgeAsync(companyId, appellant, run, "Again", new DateOnly(2026, 5, 6))).Error.Code.Should().Be("Hse.AppealExists");

        (await appeals.DecideAsync(companyId, appeal, true, new DateOnly(2026, 5, 20), "Upheld")).Error.Code.Should().Be("Hse.NoHearing");
        (await appeals.SetHearingAsync(companyId, appeal, new DateOnly(2026, 5, 19))).IsSuccess.Should().BeTrue();
        (await appeals.DecideAsync(companyId, appeal, true, new DateOnly(2026, 5, 19), "Disability points were omitted")).IsSuccess.Should().BeTrue();

        // A second stand is serviced and allocated to the appellant in a later run.
        HousingStand second = new(companyId, parcel.Id, scheme.Id, "AP-2", 300m, 5000m);
        db.HousingStands.Add(second);
        await db.SaveChangesAsync();
        string run2 = (await allocations.ProposeAsync(Proposal())).Value.RunNumber;
        string appellantCert = (await allocations.ApproveAsync(companyId, run2, 72, new DateOnly(2026, 6, 1), 30)).Value.Lines[0].CertificateNumber!;

        (await appeals.LinkReallocationAsync(companyId, appeal, winnerCert)).Error.Code.Should().Be("Hse.CertificateNotAppellants");
        (await appeals.LinkReallocationAsync(companyId, appeal, appellantCert)).IsSuccess.Should().BeTrue();
        (await appeals.RegisterAsync(companyId)).Should().Equal(
            new AppealEntry(appeal, appellant, run, new DateOnly(2026, 5, 5), AppealStatus.Upheld, new DateOnly(2026, 5, 19), appellantCert));
    }
}
