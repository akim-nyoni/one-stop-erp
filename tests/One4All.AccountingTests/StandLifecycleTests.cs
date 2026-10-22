using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the stand state machine, consolidation, re-subdivision and spouse checks: jumps and document-only moves are refused and every
/// move needs its authority; a withheld stand cannot be sold or moved on and is released back to where it was; free stands consolidate
/// (sizes and costs added, the old ones retired at nil cost) and a stand re-subdivides by area, lineage walking back to the parcel; a
/// household whose spouse is already listed or owns a stand is refused, and an applicant whose spouse comes to hold a stand is left out of
/// an allocation run.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandLifecycleTests
{
    private readonly AccountingFixture _fixture;

    public StandLifecycleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stands_follow_the_lifecycle_and_households_get_one_stand()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        HousingScheme scheme = new(companyId, $"SCH-LC-{companyId}", "Lifecycle Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        string parcel = $"LP-LC-{companyId}";
        long parcelId = (await housing.AcquireLandAsync(companyId, parcel, "Layout", 1000m, 10000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-LC")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("LC-1", 250m), new NewStand("LC-2", 250m), new NewStand("LC-3", 250m), new NewStand("LC-4", 250m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).IsSuccess.Should().BeTrue();
        IStandLifecycleService lifecycle = scope.ServiceProvider.GetRequiredService<IStandLifecycleService>();
        DateOnly day = new(2026, 2, 1);

        // Moves: no jumps, no document-only states, always an authority.
        (await lifecycle.MoveAsync(companyId, "lc-1", StandStatus.TitleApplied, day, 9, "Res 1", null)).Error.Code.Should().Be("Hse.StandMove");
        (await lifecycle.MoveAsync(companyId, "LC-1", StandStatus.Sold, day, 9, "Res 1", null)).Error.Code.Should().Be("Hse.StandMoveByDocument");
        (await lifecycle.MoveAsync(companyId, "LC-1", StandStatus.Reserved, day, 9, " ", null)).Error.Code.Should().Be("Hse.StandAuthority");
        (await lifecycle.MoveAsync(companyId, "LC-1", StandStatus.Reserved, day, 9, "Res 1", "Clinic site")).Value.Should().Be(StandStatus.Reserved);
        (await lifecycle.MoveAsync(companyId, "LC-1", StandStatus.Available, day, 9, "Res 2", "Clinic moved")).Value.Should().Be(StandStatus.Available);

        // A hold blocks the sale and every other move until it is released back.
        Customer holderOfLc2 = new(companyId, $"LC-G-{companyId}", "Grace Holder", Scenario.Usd) { NationalIdNumber = $"LC-G-{companyId}" };
        Customer buyer = new(companyId, $"LC-B-{companyId}", "First Buyer", Scenario.Usd) { NationalIdNumber = $"LC-H-{companyId}" };
        db.Customers.AddRange(holderOfLc2, buyer);
        await db.SaveChangesAsync();
        long lc2 = await db.HousingStands.Where(s => s.CompanyId == companyId && s.StandNumber == "LC-2").Select(s => s.Id).SingleAsync();
        (await lifecycle.MoveAsync(companyId, "LC-2", StandStatus.Withheld, day, 9, "Caveat CV-7", "Boundary dispute")).Value.Should().Be(StandStatus.Withheld);
        (await housing.SellStandAsync(companyId, lc2, holderOfLc2.Id, holderOfLc2.Name, 3000m, day, "4100", "1460", "5100", $"LC-INV0-{companyId}")).Error.Code.Should().Be("Housing.StandBlocked");
        (await lifecycle.MoveAsync(companyId, "LC-2", StandStatus.Reserved, day, 9, "Res 3", null)).Error.Code.Should().Be("Hse.StandWithheld");
        (await lifecycle.MoveAsync(companyId, "LC-2", StandStatus.Available, day, 9, "Caveat lifted", null)).Value.Should().Be(StandStatus.Available);

        // Consolidation of two free stands, then a re-subdivision of the result.
        decimal cost3 = await db.HousingStands.Where(s => s.CompanyId == companyId && s.StandNumber == "LC-3").Select(s => s.AllocatedCost).SingleAsync();
        (await lifecycle.ConsolidateAsync(companyId, ["LC-3"], "LC-34", day, 9, "Res 4")).Error.Code.Should().Be("Hse.Consolidation");
        (await lifecycle.ConsolidateAsync(companyId, ["LC-3", "lc-4"], "LC-34", day, 9, "Res 4")).Value.Should().Be("LC-34");
        HousingStand merged = await db.HousingStands.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "LC-34");
        (merged.SizeSquareMetres, merged.AllocatedCost, merged.Status).Should().Be((500m, cost3 * 2, StandStatus.Available));
        HousingStand gone = await db.HousingStands.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "LC-3");
        (gone.Status, gone.AllocatedCost, gone.ConsolidatedIntoStandId).Should().Be((StandStatus.ConsolidatedAway, 0m, merged.Id));
        (await lifecycle.ConsolidateAsync(companyId, ["LC-3", "LC-1"], "LC-31", day, 9, "Res 5")).Error.Code.Should().Be("Hse.StandEncumbered");
        (await lifecycle.ResubdivideAsync(companyId, "LC-34", [new StandPart("LC-34A", 300m), new StandPart("LC-34B", 150m)], day, 9, "Res 6")).Error.Code.Should().Be("Hse.ResubdivisionArea");
        (await lifecycle.ResubdivideAsync(companyId, "LC-34", [new StandPart("LC-34A", 300m), new StandPart("LC-34B", 200m)], day, 9, "Res 6")).Value.Should().Equal("LC-34A", "LC-34B");
        (await db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.StandNumber.StartsWith("LC-34")).OrderBy(s => s.StandNumber).Select(s => s.AllocatedCost).ToListAsync())
            .Should().Equal(0m, decimal.Round(cost3 * 2 * 0.6m, 2), cost3 * 2 - decimal.Round(cost3 * 2 * 0.6m, 2));

        StandHistory history = (await lifecycle.HistoryAsync(companyId, "LC-34A")).Value;
        history.Lineage.Should().Equal("LC-34A re-subdivided from LC-34", "  LC-34 consolidated from LC-3", "  LC-34 consolidated from LC-4", $"Parcel {parcel}");
        StandHistory one = (await lifecycle.HistoryAsync(companyId, "LC-1")).Value;
        one.Moves.Select(m => (m.From, m.To, m.Authority)).Should().Equal((StandStatus.Available, StandStatus.Reserved, "Res 1"), (StandStatus.Reserved, StandStatus.Available, "Res 2"));
        one.Next.Should().Contain([StandStatus.Reserved, StandStatus.Allocated, StandStatus.Withheld]);

        // Sale moves the stand through its own document and is audited too.
        (await housing.SellStandAsync(companyId, await db.HousingStands.Where(s => s.CompanyId == companyId && s.StandNumber == "LC-1").Select(s => s.Id).SingleAsync(),
            buyer.Id, buyer.Name, 3000m, day, "4100", "1460", "5100", $"LC-INV1-{companyId}")).IsSuccess.Should().BeTrue();
        (await lifecycle.HistoryAsync(companyId, "LC-1")).Value.Moves.Last().Should().Be(new StandMove(StandStatus.Available, StandStatus.Sold, day, null, "Sale", null));
        (await lifecycle.MoveAsync(companyId, "LC-1", StandStatus.FullyPaid, day, 9, "Statement nil", null)).Value.Should().Be(StandStatus.FullyPaid);

        // Spouses: one household, one stand.
        Task<Result<WaitingListRegistration>> Register(string id, int year, string? spouse)
            => TestClock.RegisterCapturedAsync(scope.ServiceProvider, new WaitingListApplication
            {
                CompanyId = companyId, IdNumber = id, FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1), ResidentSince = new DateOnly(2000, 1, 1),
                MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
                MaritalStatus = spouse is null ? MaritalStatus.Single : MaritalStatus.Married, MaritalRegime = spouse is null ? null : MaritalRegime.InCommunity, SpouseIdNumber = spouse, SpouseName = spouse,
            });
        string a = $"LC-A-{companyId}", b = $"LC-B-{companyId}", f = $"LC-F-{companyId}";
        (await Register(a, 2010, b)).IsSuccess.Should().BeTrue();
        (await Register(b, 2011, null)).Error.Code.Should().Be("Hse.SpouseOnList");
        (await Register($"LC-C-{companyId}", 2011, a)).Error.Code.Should().Be("Hse.SpouseOnList");
        (await Register($"LC-D-{companyId}", 2011, $"LC-D-{companyId}")).Error.Code.Should().Be("Hse.SpouseSelf");
        (await Register($"LC-E-{companyId}", 2011, $"LC-H-{companyId}")).Error.Code.Should().Be("Hse.NotQualified", "the spouse already owns LC-1");
        (await Register(f, 2009, $"LC-G-{companyId}")).IsSuccess.Should().BeTrue();

        // F's spouse then comes to hold LC-2: F is left out of the run although first in seniority.
        (await housing.SellStandAsync(companyId, lc2, holderOfLc2.Id, holderOfLc2.Name, 3000m, day, "4100", "1460", "5100", $"LC-INV2-{companyId}")).IsSuccess.Should().BeTrue();
        AllocationRunView run = (await scope.ServiceProvider.GetRequiredService<IAllocationService>().ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        run.Lines.Select(l => (l.StandNumber, l.WaitingListNumber)).Should().ContainSingle().Which.StandNumber.Should().Be("LC-34A");
        long fId = await db.HousingApplicants.Where(x => x.CompanyId == companyId && x.IdNumber == f).Select(x => x.Id).SingleAsync();
        (await db.Set<AllocationExclusion>().AsNoTracking().Where(x => x.ApplicantId == fId).Select(x => x.Reason).SingleAsync()).Should().Be(ExclusionReason.SpouseHolds);
    }
}
