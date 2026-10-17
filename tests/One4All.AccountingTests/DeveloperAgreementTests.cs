using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers developer agreements: scheme stands are assigned to the council and developer quotas (not beyond the quota, not
/// outside the scheme, not already promised); milestones are tracked; a council stand handed over returns to the council's
/// pool (and can then be allocated, here to a cooperative) while a developer stand stays blocked; the reconciliation shows
/// contracted vs handed over, overdue milestones and the penalty; and the penalty is invoiced to the developer as it
/// accrues, never twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeveloperAgreementTests
{
    private readonly AccountingFixture _fixture;

    public DeveloperAgreementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Developer_delivery_is_reconciled_and_late_council_stands_are_penalised()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer developer = new(companyId, "DV-DEV", "Partner Developments", Scenario.Usd);
        LandParcel parcel = new(companyId, $"LP-DV-{companyId}", "Developer parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-DV-{companyId}", "Developer Scheme", SchemeType.PublicPrivatePartnership);
        db.Customers.Add(developer);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand[] stands = [.. Enumerable.Range(1, 5).Select(i => new HousingStand(companyId, parcel.Id, scheme.Id, $"DV-{i}", 300m, 2000m))];
        HousingStand outside = new(companyId, parcel.Id, null, "DV-X", 300m, 2000m);
        db.HousingStands.AddRange(stands);
        db.HousingStands.Add(outside);
        await db.SaveChangesAsync();

        IDeveloperAgreementService developers = scope.ServiceProvider.GetRequiredService<IDeveloperAgreementService>();
        string number = (await developers.CreateAsync(new DeveloperAgreementTerms
        {
            CompanyId = companyId, SchemeId = scheme.Id, DeveloperCustomerId = developer.Id, CouncilQuota = 2, DeveloperQuota = 2,
            SignedOn = new DateOnly(2026, 1, 10), DeliveryDeadline = new DateOnly(2026, 4, 30), PenaltyPerStandPerMonth = 100m,
        })).Value;

        DateOnly assigned = new(2026, 1, 15);
        (await developers.AssignStandsAsync(companyId, number, StandQuota.Council, [stands[0].Id, stands[1].Id], assigned)).Value.Should().Be(2);
        (await developers.AssignStandsAsync(companyId, number, StandQuota.Council, [stands[2].Id], assigned)).Error.Code.Should().Be("Hse.QuotaExceeded");
        (await developers.AssignStandsAsync(companyId, number, StandQuota.Developer, [outside.Id], assigned)).Error.Code.Should().Be("Hse.StandNotInScheme");
        (await developers.AssignStandsAsync(companyId, number, StandQuota.Developer, [stands[0].Id], assigned)).Error.Code.Should().Be("Hse.StandUnavailable");
        (await developers.AssignStandsAsync(companyId, number, StandQuota.Developer, [stands[2].Id, stands[3].Id], assigned)).Value.Should().Be(2);

        (await developers.AddMilestoneAsync(companyId, number, "Roads", new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        (await developers.AddMilestoneAsync(companyId, number, "Water reticulation", new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();
        (await developers.CompleteMilestoneAsync(companyId, number, "roads", new DateOnly(2026, 3, 20))).IsSuccess.Should().BeTrue();

        // A council stand handed over goes back to the council's pool; a developer stand stays blocked.
        (await developers.HandOverAsync(companyId, number, stands[0].Id, new DateOnly(2026, 4, 20))).IsSuccess.Should().BeTrue();
        (await developers.HandOverAsync(companyId, number, stands[0].Id, new DateOnly(2026, 4, 21))).Error.Code.Should().Be("Hse.AlreadyHandedOver");
        (await developers.HandOverAsync(companyId, number, stands[2].Id, new DateOnly(2026, 5, 15))).IsSuccess.Should().BeTrue();
        (await developers.HandOverAsync(companyId, number, stands[4].Id, new DateOnly(2026, 5, 15))).Error.Code.Should().Be("Hse.NotAgreementStand");
        Dictionary<long, StandStatus> statuses = await db.HousingStands.AsNoTracking().Where(s => s.SchemeId == scheme.Id).ToDictionaryAsync(s => s.Id, s => s.Status);
        (statuses[stands[0].Id], statuses[stands[1].Id], statuses[stands[2].Id]).Should().Be((StandStatus.Available, StandStatus.Allocated, StandStatus.Allocated));

        // One council stand outstanding since 30 April: as at 15 June it is two (part) months late.
        DeveloperReconciliation june = (await developers.ReconcileAsync(companyId, number, new DateOnly(2026, 6, 15))).Value;
        june.Quotas.Should().Equal(new QuotaDelivery(StandQuota.Council, 2, 2, 1, 1), new QuotaDelivery(StandQuota.Developer, 2, 2, 1, 1));
        june.OverdueMilestones.Should().Equal("Water reticulation");
        june.PenaltyAccrued.Should().Be(200m);

        (await developers.ChargePenaltiesAsync(companyId, number, new DateOnly(2026, 6, 15), "4400")).Value.Should().Be(200m);
        (await developers.ChargePenaltiesAsync(companyId, number, new DateOnly(2026, 6, 15), "4400")).Value.Should().Be(0m);

        // Handed over on 10 July - three months late in all; the next charge invoices only the extra month.
        (await developers.HandOverAsync(companyId, number, stands[1].Id, new DateOnly(2026, 7, 10))).IsSuccess.Should().BeTrue();
        (await developers.ChargePenaltiesAsync(companyId, number, new DateOnly(2026, 8, 1), "4400")).Value.Should().Be(100m);
        DeveloperReconciliation august = (await developers.ReconcileAsync(companyId, number, new DateOnly(2026, 8, 1))).Value;
        (august.PenaltyAccrued, august.PenaltiesCharged, august.Quotas[0].Outstanding).Should().Be((300m, 300m, 0));
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == developer.Id).SumAsync(i => i.OutstandingTransaction)).Should().Be(300m);

        // The handed-over council stand is free for the council's own allocation.
        ICooperativeService cooperatives = scope.ServiceProvider.GetRequiredService<ICooperativeService>();
        (await cooperatives.RegisterAsync(companyId, "DVCOOP", "Developer Coop", "REG-DV")).IsSuccess.Should().BeTrue();
        (await cooperatives.AllocateStandsAsync(companyId, "DVCOOP", [stands[0].Id], new DateOnly(2026, 8, 2))).Value.Should().Be(1);
        (await cooperatives.AllocateStandsAsync(companyId, "DVCOOP", [stands[2].Id], new DateOnly(2026, 8, 2))).Error.Code.Should().Be("Hse.StandUnavailable");
    }
}
