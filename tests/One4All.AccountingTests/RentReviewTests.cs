using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers rent reviews: flats at 300 and a house at 500, both let; a 10% review of flats from 1 June raises the flat to
/// 330 (the house stays 500) and gives its tenancy a change from June; a review not from the first of a month and a
/// repeat for June are refused; May billed after the review still bills 300 and June 330; a vacant flat registered
/// later is let at the reviewed rent.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RentReviewTests
{
    private readonly AccountingFixture _fixture;

    public RentReviewTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reviews_raise_rents_from_their_date_only()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        Customer flatTenant = new(companyId, "RR-FLAT", "Flat Tenant", Scenario.Usd);
        Customer houseTenant = new(companyId, "RR-HSE", "House Tenant", Scenario.Usd);
        db.Customers.AddRange(flatTenant, houseTenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "RR-F1", "Flat 1", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "RR-F2", "Flat 2", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "RR-H1", "House 1", RentalUnitType.House, 3, 500m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        string flat = (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "RR-F1", TenantCustomerId = flatTenant.Id, StartDate = new DateOnly(2026, 4, 1) })).Value;
        string house = (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "RR-H1", TenantCustomerId = houseTenant.Id, StartDate = new DateOnly(2026, 4, 1) })).Value;

        (await rentals.ReviewRentsAsync(companyId, new DateOnly(2026, 6, 15), 10m, RentalUnitType.Flat)).Error.Code.Should().Be("Hse.RentReview");
        (await rentals.ReviewRentsAsync(companyId, new DateOnly(2026, 6, 1), 10m, RentalUnitType.Flat)).Value.Should().Be(new RentReviewResult(2, 1));
        (await rentals.ReviewRentsAsync(companyId, new DateOnly(2026, 6, 1), 10m, RentalUnitType.Flat)).Error.Code.Should().Be("Hse.RentReviewed");

        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 5, 1))).Value.TotalRent.Should().Be(800m);   // flat 300 + house 500
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 6, 1))).Value.TotalRent.Should().Be(830m);   // flat 330 + house 500
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"{flat}-202606")).OriginalBase.Should().Be(330m);
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"{house}-202606")).OriginalBase.Should().Be(500m);

        string later = (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "RR-F2", TenantCustomerId = houseTenant.Id, StartDate = new DateOnly(2026, 7, 1) })).Value;
        (await db.Set<Tenancy>().AsNoTracking().SingleAsync(t => t.CompanyId == companyId && t.TenancyNumber == later)).MonthlyRent.Should().Be(330m);
    }
}
