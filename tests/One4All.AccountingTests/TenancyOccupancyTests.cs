using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers occupants and lodger permits: household members are listed within the unit's occupancy limit, a lodger needs a permit that leaves the
/// tenant a room, the permit's fee is billed with the rent while it is in force, and the household and permits end with the tenancy.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TenancyOccupancyTests
{
    private readonly AccountingFixture _fixture;

    public TenancyOccupancyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Occupants_and_lodgers_are_permitted_limited_and_billed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4980", "Lodger Fees", AccountType.Income));
        Customer tenant = new(companyId, "OC-T1", "Tenant One", Scenario.Usd);
        db.Customers.Add(tenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        IRentalOccupancyService occupancy = scope.ServiceProvider.GetRequiredService<IRentalOccupancyService>();
        (await rentals.AddUnitAsync(companyId, "OC-F1", "Three-room flat", RentalUnitType.Flat, 3, 300m, "4000", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        string tenancy = (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "OC-F1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 1) })).Value;
        (await occupancy.SetMaxOccupantsAsync(companyId, "OC-F1", 4)).IsSuccess.Should().BeTrue();

        // Tenant, spouse and child: three of four.
        (await occupancy.AddOccupantAsync(companyId, tenancy, "Spouse One", null, "Spouse", new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();
        long child = (await occupancy.AddOccupantAsync(companyId, tenancy, "Child One", null, "Child", new DateOnly(2026, 3, 1))).Value;

        // A lodger may have at most two of the three rooms; one room at 50 a month from April.
        LodgerPermitRequest Permit(int rooms) => new(companyId, tenancy, "Lodger Person", "63-123456X63", rooms, 50m, "4980", new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31));
        (await occupancy.IssueLodgerPermitAsync(Permit(3))).Error.Code.Should().Be("Hse.LodgerRooms");
        string permit = (await occupancy.IssueLodgerPermitAsync(Permit(1))).Value;

        // Four now live there: no more until someone leaves.
        (await occupancy.AddOccupantAsync(companyId, tenancy, "Cousin One", null, "Relative", new DateOnly(2026, 4, 15))).Error.Code.Should().Be("Hse.Overcrowded");
        (await occupancy.RemoveOccupantAsync(companyId, tenancy, child, new DateOnly(2026, 5, 1))).IsSuccess.Should().BeTrue();
        (await occupancy.AddOccupantAsync(companyId, tenancy, "Cousin One", null, "Relative", new DateOnly(2026, 5, 10))).IsSuccess.Should().BeTrue();

        // The fee rides on the rent while the permit is in force: April and May (revoked 20 May), not June.
        (await occupancy.RevokeLodgerPermitAsync(companyId, permit, new DateOnly(2026, 5, 20), "Lodger left")).IsSuccess.Should().BeTrue();
        foreach (int month in new[] { 3, 4, 5, 6 })
        {
            (await rentals.RunRentAsync(companyId, new DateOnly(2026, month, 1))).IsSuccess.Should().BeTrue();
        }

        TenancyOccupancy june = (await occupancy.OccupancyAsync(companyId, tenancy, new DateOnly(2026, 6, 1))).Value;
        (june.Occupants, june.Lodgers, june.People, june.MaxOccupants).Should().BeEquivalentTo((new[] { "Spouse One", "Cousin One" }, Array.Empty<string>(), 3, (int?)4));

        // Ending the tenancy moves the household out.
        (await rentals.EndTenancyAsync(companyId, tenancy, new DateOnly(2026, 6, 30), "1100")).IsSuccess.Should().BeTrue();
        (await occupancy.OccupancyAsync(companyId, tenancy, new DateOnly(2026, 7, 1))).Value.People.Should().Be(0);
        (await occupancy.AddOccupantAsync(companyId, tenancy, "Late Arrival", null, "Relative", new DateOnly(2026, 7, 2))).Error.Code.Should().Be("Hse.TenancyEnded");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 12, 31))).Value;
        decimal Net(string code) => tb.Rows.Where(r => r.AccountCode == code).Sum(r => r.Debit - r.Credit);
        (Net("4000"), Net("4980")).Should().Be((-1_200m, -100m));
    }
}
