using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers caveats and blocks: only an officer with the caveat permission can block or lift; a blocked stand is left out
/// of an allocation run, refused for a cooperative, a sale, a transfer and a lease, and a run approved after one of its
/// stands is blocked is refused; the officer who placed a block cannot lift it; lifted, the stand is free again and the
/// block stays on the stand's history.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandBlockTests
{
    private readonly AccountingFixture _fixture;

    public StandBlockTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blocked_stands_cannot_be_allocated_sold_leased_or_transferred()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role officerRole = new($"Caveat officer {companyId}");
        officerRole.Grant("Housing.Caveat", OperationFlags.View | OperationFlags.Create | OperationFlags.Delete);
        db.Roles.Add(officerRole);
        User placer = new($"blk-placer-{companyId}", "Placing Officer");
        User lifter = new($"blk-lifter-{companyId}", "Lifting Officer");
        User clerk = new($"blk-clerk-{companyId}", "Housing Clerk");
        db.Users.AddRange(placer, lifter, clerk);
        Customer buyer = new(companyId, "BK-BUY", "Block Buyer", Scenario.Usd);
        db.Customers.Add(buyer);
        LandParcel parcel = new(companyId, $"LP-BK-{companyId}", "Block parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-BK-{companyId}", "Block Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        placer.AssignRole(officerRole.Id);
        lifter.AssignRole(officerRole.Id);
        HousingStand blocked = new(companyId, parcel.Id, scheme.Id, "BK-1", 300m, 5000m);
        HousingStand free = new(companyId, parcel.Id, scheme.Id, "BK-2", 300m, 5000m);
        db.HousingStands.AddRange(blocked, free);
        await db.SaveChangesAsync();

        IStandBlockService blocks = scope.ServiceProvider.GetRequiredService<IStandBlockService>();
        DateOnly day = new(2026, 3, 1);
        (await blocks.PlaceAsync(companyId, blocked.Id, BlockReason.CourtInterdict, "Interdict pending", "HC 123/26", clerk.Id, day)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await blocks.PlaceAsync(companyId, blocked.Id, BlockReason.CourtInterdict, "Interdict pending", "", placer.Id, day)).Error.Code.Should().Be("Hse.BlockDetails");
        (await blocks.PlaceAsync(companyId, blocked.Id, BlockReason.CourtInterdict, "Interdict pending", "HC 123/26", placer.Id, day)).IsSuccess.Should().BeTrue();
        (await blocks.PlaceAsync(companyId, blocked.Id, BlockReason.FamilyDispute, "Second", "X-1", placer.Id, day)).Error.Code.Should().Be("Hse.AlreadyBlocked");

        // Every route that would hand the stand to someone refuses it.
        IWaitingListService list = scope.ServiceProvider.GetRequiredService<IWaitingListService>();
        foreach ((string id, int year) in new[] { ("BK-A", 2015), ("BK-B", 2016) })
        {
            (await list.RegisterAsync(new WaitingListApplication
            {
                CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
                ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
            })).IsSuccess.Should().BeTrue();
        }

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = day, UserId = 71,
        })).Value;
        run.Lines.Select(l => l.StandNumber).Should().Equal("BK-2");

        ICooperativeService cooperatives = scope.ServiceProvider.GetRequiredService<ICooperativeService>();
        (await cooperatives.RegisterAsync(companyId, "BKCOOP", "Block Coop", "REG-BK")).IsSuccess.Should().BeTrue();
        (await cooperatives.AllocateStandsAsync(companyId, "BKCOOP", [blocked.Id], day)).Error.Code.Should().Be("Hse.StandUnavailable");

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        (await housing.SellStandAsync(companyId, blocked.Id, buyer.Id, buyer.Name, 6000m, day, "4100", "1400", "5000", $"BK-INV-{companyId}")).Error.Code.Should().Be("Housing.StandBlocked");
        (await housing.TransferOwnershipAsync(companyId, blocked.Id, buyer.Id, buyer.Name, 0m, day, true, true)).Error.Code.Should().Be("Housing.StandBlocked");

        IStandLeaseService leases = scope.ServiceProvider.GetRequiredService<IStandLeaseService>();
        (await leases.CreateAsync(new StandLeaseRequest
        {
            CompanyId = companyId, StandId = blocked.Id, LesseeCustomerId = buyer.Id, StartDate = new DateOnly(2026, 4, 1), TermYears = 10, ReviewEveryYears = 5,
            AnnualRent = 1200m, IncomeAccountCode = "4400", AccruedIncomeAccountCode = "1100",
        })).Error.Code.Should().Be("Hse.StandBlocked");

        // A stand blocked after the run was proposed stops its approval.
        (await blocks.PlaceAsync(companyId, free.Id, BlockReason.DoubleAllocation, "Investigating a second certificate", "INV-7", placer.Id, day)).IsSuccess.Should().BeTrue();
        (await allocations.ApproveAsync(companyId, run.RunNumber, 72, day, 30)).Error.Code.Should().Be("Hse.StandBlocked");

        // Lifting: by a different officer with the permission, with reasons.
        DateOnly later = new(2026, 4, 1);
        (await blocks.LiftAsync(companyId, blocked.Id, "Interdict discharged", "HC 123/26 order 2", placer.Id, later)).Error.Code.Should().Be("Hse.SelfLift");
        (await blocks.LiftAsync(companyId, blocked.Id, "Interdict discharged", "HC 123/26 order 2", clerk.Id, later)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await blocks.LiftAsync(companyId, blocked.Id, "Interdict discharged", "HC 123/26 order 2", lifter.Id, later)).IsSuccess.Should().BeTrue();
        (await blocks.LiftAsync(companyId, blocked.Id, "Again", "X", lifter.Id, later)).Error.Code.Should().Be("Hse.NotBlocked");

        (await blocks.InForceAsync(companyId)).Select(b => b.StandNumber).Should().Equal("BK-2");
        IReadOnlyList<StandBlockEntry> history = await blocks.HistoryAsync(companyId, blocked.Id);
        history.Should().ContainSingle();
        (history[0].Reason, history[0].PlacedByUserId, history[0].LiftedByUserId, history[0].LiftReason).Should().Be((BlockReason.CourtInterdict, placer.Id, (long?)lifter.Id, "Interdict discharged"));

        (await cooperatives.AllocateStandsAsync(companyId, "BKCOOP", [blocked.Id], later)).Value.Should().Be(1);
    }
}
