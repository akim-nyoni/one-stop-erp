using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers informal-settlement regularisation: a settlement is registered and enumerated (one exercise open at a time, a
/// person counted once); households occupying after the cut-off or headed by a minor do not qualify; no household is
/// regularised before the upgrading plan is approved; an eligible household is allocated a stand directly, and that stand
/// cannot then go to anyone else; another is registered on the waiting list with informal-settlement tenure; a household
/// whose head already holds a cooperative stand is refused; and the summary counts each outcome.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InformalSettlementTests
{
    private readonly AccountingFixture _fixture;

    public InformalSettlementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Enumerated_households_are_regularised_or_waiting_listed_without_double_allocation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-IS-{companyId}", "Settlement parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand standOne = new(companyId, parcel.Id, null, "IS-1", 200m, 1000m);
        HousingStand standTwo = new(companyId, parcel.Id, null, "IS-2", 200m, 1000m);
        db.HousingStands.AddRange(standOne, standTwo);
        await db.SaveChangesAsync();

        // The head of household E already holds a cooperative stand (IS-2).
        ICooperativeService cooperatives = scope.ServiceProvider.GetRequiredService<ICooperativeService>();
        (await cooperatives.RegisterAsync(companyId, "ISCOOP", "Settlement Coop", "REG-IS")).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "ISCOOP", "ID-E", "Head E", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await cooperatives.AllocateStandsAsync(companyId, "ISCOOP", [standTwo.Id], new DateOnly(2026, 1, 2))).IsSuccess.Should().BeTrue();
        (await cooperatives.SubAllocateAsync(companyId, "ISCOOP", standTwo.Id, "ID-E", new DateOnly(2026, 1, 3))).IsSuccess.Should().BeTrue();

        IInformalSettlementService settlements = scope.ServiceProvider.GetRequiredService<IInformalSettlementService>();
        (await settlements.RegisterAsync(new SettlementRegistration
        {
            CompanyId = companyId, Code = "ext7", Name = "Extension 7", Ward = "Ward 12", LandParcelId = parcel.Id, CutOffDate = new DateOnly(2025, 12, 31),
        })).IsSuccess.Should().BeTrue();
        DateOnly surveyDay = new(2026, 3, 1);
        (await settlements.EnumerateAsync(Survey(companyId, "ID-A", "Head A", 1980, 2018, surveyDay))).Error.Code.Should().Be("Hse.NoOpenExercise");
        (await settlements.StartEnumerationAsync(companyId, "EXT7", "ENUM-2026-1", surveyDay)).IsSuccess.Should().BeTrue();
        (await settlements.StartEnumerationAsync(companyId, "EXT7", "ENUM-2026-2", surveyDay)).Error.Code.Should().Be("Hse.ExerciseOpen");

        EnumerationResult a = (await settlements.EnumerateAsync(Survey(companyId, "ID-A", "Head A", 1980, 2018, surveyDay))).Value;
        EnumerationResult b = (await settlements.EnumerateAsync(Survey(companyId, "ID-B", "Head B", 1985, 2026, surveyDay))).Value;
        EnumerationResult c = (await settlements.EnumerateAsync(Survey(companyId, "ID-C", "Head C", 2012, 2020, surveyDay))).Value;
        EnumerationResult d = (await settlements.EnumerateAsync(Survey(companyId, "ID-D", "Head D", 1975, 2015, surveyDay))).Value;
        EnumerationResult e = (await settlements.EnumerateAsync(Survey(companyId, "ID-E", "Head E", 1990, 2017, surveyDay))).Value;
        (await settlements.EnumerateAsync(Survey(companyId, "id-a", "Head A again", 1980, 2018, surveyDay))).Error.Code.Should().Be("Hse.AlreadyEnumerated");
        new[] { a.IsEligible, b.IsEligible, c.IsEligible, d.IsEligible, e.IsEligible }.Should().Equal(true, false, false, true, true);
        b.IneligibleReason.Should().Contain("after the cut-off date");
        c.IneligibleReason.Should().Contain("under the minimum age");
        (await settlements.CloseEnumerationAsync(companyId, "EXT7", new DateOnly(2026, 3, 31))).Value.Should().Be(5);

        // No regularisation until the upgrading plan is approved.
        DateOnly day = new(2026, 5, 1);
        (await settlements.RegulariseAsync(companyId, a.HouseholdNumber, standOne.Id, day)).Error.Code.Should().Be("Hse.NoUpgradingPlan");
        (await settlements.ApprovePlanAsync(companyId, "EXT7", "In-situ upgrade: re-block, survey, service and regularise", new DateOnly(2026, 4, 15))).IsSuccess.Should().BeTrue();

        (await settlements.RegulariseAsync(companyId, a.HouseholdNumber, standOne.Id, day)).IsSuccess.Should().BeTrue();
        (await settlements.RegulariseAsync(companyId, a.HouseholdNumber, standOne.Id, day)).Error.Code.Should().Be("Hse.HouseholdDealtWith");
        (await settlements.RegulariseAsync(companyId, d.HouseholdNumber, standOne.Id, day)).Error.Code.Should().Be("Hse.StandUnavailable");
        (await settlements.RegulariseAsync(companyId, b.HouseholdNumber, standOne.Id, day)).Error.Code.Should().Be("Hse.HouseholdIneligible");
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == standOne.Id)).Status.Should().Be(StandStatus.Allocated);

        // A cooperative stand is not free for regularisation, and a cooperative member is already housed.
        HousingStand standThree = new(companyId, parcel.Id, null, "IS-3", 200m, 1000m);
        db.HousingStands.Add(standThree);
        await db.SaveChangesAsync();
        (await settlements.RegulariseAsync(companyId, d.HouseholdNumber, standTwo.Id, day)).Error.Code.Should().Be("Hse.StandUnavailable");
        (await settlements.RegulariseAsync(companyId, e.HouseholdNumber, standThree.Id, day)).Error.Code.Should().Be("Hse.AlreadyHoused");

        string waitingListNumber = (await settlements.ConvertToWaitingListAsync(companyId, d.HouseholdNumber, HousingCategory.HighDensity, day)).Value;
        HousingApplicant applicant = await db.HousingApplicants.AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.WaitingListNumber == waitingListNumber);
        (applicant.IdNumber, applicant.Tenure, applicant.Dependants).Should().Be(("ID-D", CurrentTenure.InformalSettlement, 3));
        (await settlements.ConvertToWaitingListAsync(companyId, d.HouseholdNumber, HousingCategory.HighDensity, day)).Error.Code.Should().Be("Hse.HouseholdDealtWith");

        SettlementSummary summary = (await settlements.SummaryAsync(companyId, "ext7")).Value;
        summary.Should().Be(new SettlementSummary("EXT7", "Extension 7", new DateOnly(2026, 4, 15), 5, 3, 1, 1, 1));
    }

    private static HouseholdSurvey Survey(long companyId, string id, string name, int bornYear, int occupiedYear, DateOnly date) => new()
    {
        CompanyId = companyId, SettlementCode = "EXT7", HeadIdNumber = id, HeadName = name, HeadDateOfBirth = new DateOnly(bornYear, 6, 1), HouseholdSize = 4,
        MonthlyIncome = 300m, OccupiedSince = new DateOnly(occupiedYear, 2, 1), StructureType = StructureType.Shack, StructureReference = $"STR-{id}", Date = date,
    };
}
