using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sectional title: a building is registered as units with quotas from their floor areas (once per stand); units
/// are transferred individually; the body corporate levy is split by quota and invoiced to the owners once per period,
/// the share of an unowned unit reported unbilled; and a caveat on the stand stops unit transfers.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SectionalTitleTests
{
    private readonly AccountingFixture _fixture;

    public SectionalTitleTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Units_carry_quotas_transfer_individually_and_share_the_levy()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer first = new(companyId, "ST-X", "Unit Owner X", Scenario.Usd);
        Customer second = new(companyId, "ST-Y", "Unit Owner Y", Scenario.Usd);
        db.Customers.AddRange(first, second);
        LandParcel parcel = new(companyId, $"LP-ST-{companyId}", "Flats parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, "ST-1", 1200m, 9000m);
        db.HousingStands.Add(stand);
        await db.SaveChangesAsync();

        ISectionalTitleService sectional = scope.ServiceProvider.GetRequiredService<ISectionalTitleService>();
        (await sectional.RegisterAsync(companyId, "blk", "Block A Flats", stand.Id, [new("1a", 50m), new("1A", 75m)])).Error.Code.Should().Be("Hse.SectionalScheme");
        IReadOnlyList<SectionalUnitEntry> units = (await sectional.RegisterAsync(companyId, "blk", "Block A Flats", stand.Id, [new("1a", 50m), new("1b", 75m), new("2a", 75m)])).Value;
        units.Select(u => (u.UnitNumber, u.ParticipationQuota)).Should().Equal(("1A", 25m), ("1B", 37.5m), ("2A", 37.5m));
        (await sectional.RegisterAsync(companyId, "OTHER", "Same stand", stand.Id, [new("1", 10m)])).Error.Code.Should().Be("Hse.SectionalExists");

        (await sectional.TransferUnitAsync(companyId, "BLK", "1a", first.Id, 30000m, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        (await sectional.TransferUnitAsync(companyId, "BLK", "1B", second.Id, 42000m, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        (await sectional.TransferUnitAsync(companyId, "BLK", "9Z", second.Id, 1m, new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Hse.UnitNotFound");

        // March's levy: the owners pay their quota; the unowned unit's share is reported, not billed.
        SectionalLevyResult march = (await sectional.RaiseLevyAsync(companyId, "BLK", 202603, 1000m, "4400", new DateOnly(2026, 3, 1))).Value;
        march.Billed.Should().Equal(new UnitAmount("1A", 250m), new UnitAmount("1B", 375m));
        march.Unbilled.Should().Be(375m);
        (await sectional.RaiseLevyAsync(companyId, "BLK", 202603, 1000m, "4400", new DateOnly(2026, 3, 2))).Error.Code.Should().Be("Hse.LevyRaised");
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == first.Id).SumAsync(i => i.OutstandingTransaction)).Should().Be(250m);

        // A unit changes hands on its own; the stand and the other units are untouched.
        (await sectional.TransferUnitAsync(companyId, "BLK", "1A", second.Id, 33000m, new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Hse.UnitTransferDate");
        (await sectional.TransferUnitAsync(companyId, "BLK", "1B", second.Id, 1m, new DateOnly(2026, 4, 1))).Error.Code.Should().Be("Hse.SameOwner");
        (await sectional.TransferUnitAsync(companyId, "BLK", "1A", second.Id, 33000m, new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await sectional.UnitsAsync(companyId, "BLK")).Value.Select(u => (u.UnitNumber, u.Owner)).Should().Equal(("1A", "Unit Owner Y"), ("1B", "Unit Owner Y"), ("2A", (string?)null));
        (await db.Set<UnitOwnership>().AsNoTracking().CountAsync(o => o.CustomerId == first.Id && o.OwnedTo == new DateOnly(2026, 3, 31))).Should().Be(1);

        db.Set<StandBlock>().Add(new StandBlock(companyId, stand.Id, BlockReason.CourtInterdict, "Body corporate dispute", "HC 9/26", 1, new DateOnly(2026, 4, 5)));
        await db.SaveChangesAsync();
        (await sectional.TransferUnitAsync(companyId, "BLK", "2A", first.Id, 40000m, new DateOnly(2026, 4, 10))).Error.Code.Should().Be("Hse.StandBlocked");
    }
}
