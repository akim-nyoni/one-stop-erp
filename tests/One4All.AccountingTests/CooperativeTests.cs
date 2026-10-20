using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers cooperative allocations: a cooperative with three members (a duplicate ID refused) is allocated two stands,
/// which become allocated (a stand cannot go to a second cooperative); one stand is sub-allocated to a member; the same
/// member cannot take a second stand, a member who has left cannot take one, the other active member takes the second;
/// a stand outside the cooperative cannot be sub-allocated; the register shows both levels.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CooperativeTests
{
    private readonly AccountingFixture _fixture;

    public CooperativeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stands_go_to_the_cooperative_then_one_each_to_its_members()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-CO-{companyId}", "Coop parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand a = new(companyId, parcel.Id, null, "CO-1", 300m, 5000m);
        HousingStand b = new(companyId, parcel.Id, null, "CO-2", 300m, 5000m);
        HousingStand outside = new(companyId, parcel.Id, null, "CO-9", 300m, 5000m);
        db.HousingStands.AddRange(a, b, outside);
        await db.SaveChangesAsync();

        ICooperativeService cooperatives = scope.ServiceProvider.GetRequiredService<ICooperativeService>();
        (await cooperatives.RegisterAsync(companyId, "unity", "Unity Housing Cooperative", "COOP/2024/77")).IsSuccess.Should().BeTrue();
        (await cooperatives.RegisterAsync(companyId, "OTHER", "Other Cooperative", "COOP/2024/78")).IsSuccess.Should().BeTrue();
        DateOnly joined = new(2025, 1, 1);
        (await cooperatives.AddMemberAsync(companyId, "UNITY", "63-100 A", "Member One", joined)).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "UNITY", "63-200", "Member Two", joined)).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "UNITY", "63-300", "Member Three", joined)).IsSuccess.Should().BeTrue();
        (await cooperatives.AddMemberAsync(companyId, "UNITY", "63-100A", "Same Person", joined)).Error.Code.Should().Be("Hse.MemberExists");

        DateOnly date = new(2026, 3, 1);
        (await cooperatives.AllocateStandsAsync(companyId, "UNITY", [a.Id, b.Id], date, 71, 71)).Error.Code.Should().Be("Hse.SelfApproval");
        (await cooperatives.AllocateStandsAsync(companyId, "UNITY", [a.Id, b.Id], date, 71, 72)).Value.Should().Be(2);
        (await cooperatives.AllocateStandsAsync(companyId, "OTHER", [a.Id], date, 71, 72)).Error.Code.Should().Be("Hse.StandUnavailable");
        (await db.HousingStands.AsNoTracking().Where(s => s.Id == a.Id || s.Id == b.Id).Select(s => s.Status).ToListAsync()).Should().OnlyContain(s => s == StandStatus.Allocated);

        (await cooperatives.SubAllocateAsync(companyId, "UNITY", a.Id, "63-100A", new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await cooperatives.SubAllocateAsync(companyId, "UNITY", b.Id, "63-100A", new DateOnly(2026, 4, 1))).Error.Code.Should().Be("Hse.MemberHoldsStand");
        (await cooperatives.RemoveMemberAsync(companyId, "UNITY", "63-300")).IsSuccess.Should().BeTrue();
        (await cooperatives.SubAllocateAsync(companyId, "UNITY", b.Id, "63-300", new DateOnly(2026, 4, 2))).Error.Code.Should().Be("Hse.NotActiveMember");
        (await cooperatives.SubAllocateAsync(companyId, "UNITY", outside.Id, "63-200", new DateOnly(2026, 4, 2))).Error.Code.Should().Be("Hse.NotCooperativeStand");
        (await cooperatives.SubAllocateAsync(companyId, "UNITY", b.Id, "63-200", new DateOnly(2026, 4, 3))).IsSuccess.Should().BeTrue();
        (await cooperatives.SubAllocateAsync(companyId, "UNITY", b.Id, "63-200", new DateOnly(2026, 4, 3))).Error.Code.Should().Be("Hse.StandSubAllocated");

        (await cooperatives.StandsAsync(companyId, "unity")).Value.Should().Equal(
            new CooperativeStandEntry("CO-1", date, "Member One", new DateOnly(2026, 4, 1)),
            new CooperativeStandEntry("CO-2", date, "Member Two", new DateOnly(2026, 4, 3)));
    }
}
