using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #14: where a scheme allows a household two stands, an allocated applicant is offered a second; where it allows one, they are left out.</summary>
[Collection(AccountingCollection.Name)]
public sealed class StandsPerBeneficiaryTests
{
    private readonly AccountingFixture _fixture;

    public StandsPerBeneficiaryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_second_stand_follows_the_scheme_limit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-SPB-{companyId}", "Two-stand parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-SPB-{companyId}", "Two-stand Scheme", SchemeType.CouncilDelivered) { StandsPerBeneficiary = 2 };
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        HousingApplicant applicant = new(companyId, $"SPB-{companyId}", "Farai Zulu", new DateOnly(2015, 1, 1));
        db.HousingApplicants.Add(applicant);
        await db.SaveChangesAsync();
        db.HousingStands.AddRange(new HousingStand(companyId, parcel.Id, scheme.Id, $"SPB-1-{companyId}", 300m, 1000m), new HousingStand(companyId, parcel.Id, scheme.Id, $"SPB-2-{companyId}", 300m, 1000m));
        applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1985, 1, 1), new DateOnly(2000, 1, 1), 2, false, 400m, CurrentTenure.Lodger);
        applicant.Register($"WL-SPB-{companyId}", applicant.RegistrationDate);
        await db.SaveChangesAsync();

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        Task<One4All.Core.Common.Result<AllocationRunView>> Propose(DateOnly on) => allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Ballot, Seed = 3, AsAtDate = on, UserId = 71,
        });
        async Task<AllocationRunView> Run(DateOnly on) => (await Propose(on)).Value;

        AllocationRunView first = await Run(new DateOnly(2026, 3, 1));
        first.Lines.Should().HaveCount(1, "one stand to an applicant in a run");
        string certificate = (await allocations.ApproveAsync(companyId, first.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).Value.Lines[0].CertificateNumber!;
        (await allocations.AcceptAsync(companyId, certificate, new DateOnly(2026, 3, 5))).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.Id == applicant.Id)).Status.Should().Be(ApplicantStatus.Allocated);

        // The scheme allows two: the allocated applicant is offered the second stand.
        AllocationRunView second = await Run(new DateOnly(2026, 4, 1));
        second.Lines.Should().HaveCount(1, "the household holds one of its two");
        (await allocations.ApproveAsync(companyId, second.RunNumber, 72, new DateOnly(2026, 4, 1), 30)).IsSuccess.Should().BeTrue("two live allocations for one applicant are allowed");

        // At two, a third stand is not offered.
        db.HousingStands.Add(new HousingStand(companyId, parcel.Id, scheme.Id, $"SPB-3-{companyId}", 300m, 1000m));
        await db.SaveChangesAsync();
        (await Propose(new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Hse.NothingToAllocate");
    }
}
