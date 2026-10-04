using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers joint ownership: holders registered with shares (the current holder among them); survivorship under joint
/// tenancy; a share transferred on its own under tenancy in common, and a deceased holder's share kept for the estate then
/// passed on; a share transfer severing a joint tenancy so a later death no longer passes by survivorship; a blocked
/// stand's shares frozen; and the holders on any date from the history.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JointOwnershipTests
{
    private readonly AccountingFixture _fixture;

    public JointOwnershipTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Shares_pass_by_transfer_survivorship_or_estate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer a = new(companyId, "JO-A", "Holder A", Scenario.Usd);
        Customer b = new(companyId, "JO-B", "Holder B", Scenario.Usd);
        Customer c = new(companyId, "JO-C", "Holder C", Scenario.Usd);
        Customer d = new(companyId, "JO-D", "Holder D", Scenario.Usd);
        db.Customers.AddRange(a, b, c, d);
        LandParcel parcel = new(companyId, $"LP-JO-{companyId}", "Joint parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand[] stands = [.. Enumerable.Range(1, 3).Select(i => new HousingStand(companyId, parcel.Id, null, $"JO-{i}", 300m, 1000m))];
        db.HousingStands.AddRange(stands);
        await db.SaveChangesAsync();
        foreach (HousingStand stand in stands)
        {
            stand.Sell(a.Id, a.Name, 5000m, new DateOnly(2026, 1, 5));
        }

        await db.SaveChangesAsync();

        IJointOwnershipService joint = scope.ServiceProvider.GetRequiredService<IJointOwnershipService>();
        DateOnly registered = new(2026, 1, 10);
        (await joint.RegisterAsync(companyId, stands[0].Id, CoOwnershipRegime.JointWithSurvivorship, [new(b.Id, 50m), new(c.Id, 50m)], registered)).Error.Code.Should().Be("Hse.HolderNotListed");
        (await joint.RegisterAsync(companyId, stands[0].Id, CoOwnershipRegime.JointWithSurvivorship, [new(a.Id, 50m), new(b.Id, 40m)], registered)).Error.Code.Should().Be("Hse.Holders");
        (await joint.RegisterAsync(companyId, stands[0].Id, CoOwnershipRegime.JointWithSurvivorship, [new(a.Id, 50m), new(b.Id, 50m)], registered)).IsSuccess.Should().BeTrue();
        (await joint.RegisterAsync(companyId, stands[0].Id, CoOwnershipRegime.InCommon, [new(a.Id, 100m)], registered)).Error.Code.Should().Be("Hse.JointRegistered");

        // Joint tenancy: B dies and A takes the whole by survivorship; the February view is unchanged.
        IReadOnlyList<StandHolderEntry> afterDeath = (await joint.RecordDeathAsync(companyId, stands[0].Id, b.Id, new DateOnly(2026, 3, 1))).Value;
        afterDeath.Select(h => (h.Name, h.SharePercent, h.Deceased)).Should().Equal(("Holder A", 100m, false));
        (await joint.HoldersAsync(companyId, stands[0].Id, new DateOnly(2026, 2, 1))).Select(h => (h.Name, h.SharePercent)).Should().Equal(("Holder A", 50m), ("Holder B", 50m));

        // Tenancy in common: C sells half her share to D; C dies and her share waits for the estate, then passes to D.
        (await joint.RegisterAsync(companyId, stands[1].Id, CoOwnershipRegime.InCommon, [new(a.Id, 60m), new(c.Id, 40m)], registered)).IsSuccess.Should().BeTrue();
        (await joint.TransferShareAsync(companyId, stands[1].Id, c.Id, d.Id, 20m, registered)).Error.Code.Should().Be("Hse.HoldingDate");
        (await joint.TransferShareAsync(companyId, stands[1].Id, c.Id, d.Id, 45m, new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Hse.ShareTransfer");
        (await joint.TransferShareAsync(companyId, stands[1].Id, c.Id, d.Id, 20m, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        (await joint.HoldersAsync(companyId, stands[1].Id, new DateOnly(2026, 2, 1))).Select(h => (h.Name, h.SharePercent)).Should().Equal(("Holder A", 60m), ("Holder C", 20m), ("Holder D", 20m));
        IReadOnlyList<StandHolderEntry> estate = (await joint.RecordDeathAsync(companyId, stands[1].Id, c.Id, new DateOnly(2026, 4, 1))).Value;
        estate.Select(h => (h.Name, h.SharePercent, h.Deceased)).Should().Equal(("Holder A", 60m, false), ("Holder C", 20m, true), ("Holder D", 20m, false));
        (await joint.RecordDeathAsync(companyId, stands[1].Id, c.Id, new DateOnly(2026, 4, 2))).Error.Code.Should().Be("Hse.NotAHolder");
        (await joint.TransferShareAsync(companyId, stands[1].Id, c.Id, d.Id, 20m, new DateOnly(2026, 5, 1))).IsSuccess.Should().BeTrue(); // the estate's share inherited
        (await joint.HoldersAsync(companyId, stands[1].Id, new DateOnly(2026, 5, 1))).Select(h => (h.Name, h.SharePercent)).Should().Equal(("Holder A", 60m), ("Holder D", 40m));

        // A share sold on its own severs a joint tenancy: B's later death leaves her share with her estate.
        (await joint.RegisterAsync(companyId, stands[2].Id, CoOwnershipRegime.JointWithSurvivorship, [new(a.Id, 50m), new(b.Id, 50m)], registered)).IsSuccess.Should().BeTrue();
        (await joint.TransferShareAsync(companyId, stands[2].Id, a.Id, c.Id, 25m, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        (await joint.RecordDeathAsync(companyId, stands[2].Id, b.Id, new DateOnly(2026, 3, 1))).Value
            .Select(h => (h.Name, h.SharePercent, h.Deceased)).Should().Equal(("Holder B", 50m, true), ("Holder A", 25m, false), ("Holder C", 25m, false));

        // A caveat freezes the shares.
        db.Set<StandBlock>().Add(new StandBlock(companyId, stands[2].Id, BlockReason.FamilyDispute, "Estate dispute", "AFF-1", 1, new DateOnly(2026, 3, 5)));
        await db.SaveChangesAsync();
        (await joint.TransferShareAsync(companyId, stands[2].Id, a.Id, c.Id, 5m, new DateOnly(2026, 3, 10))).Error.Code.Should().Be("Hse.StandBlocked");
    }
}
