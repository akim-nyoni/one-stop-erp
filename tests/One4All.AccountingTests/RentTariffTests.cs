using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers rent tariffs: a general flat rate and a cheaper rate for low-income households in one suburb; applying them from June moves both flats'
/// asking rent and the low-income tenancy's rent, leaves the house (no tariff) alone, and a rerun changes nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RentTariffTests
{
    private readonly AccountingFixture _fixture;

    public RentTariffTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tariffs_set_rents_by_unit_type_suburb_and_income()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4975", "Rental Income", AccountType.Income));
        Customer flatTenant = new(companyId, "RT-FLAT", "Flat Tenant", Scenario.Usd), houseTenant = new(companyId, "RT-HSE", "House Tenant", Scenario.Usd);
        db.Customers.AddRange(flatTenant, houseTenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "RT-F1", "Flat 1", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "RT-F2", "Flat 2", RentalUnitType.Flat, 2, 300m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "RT-H1", "House 1", RentalUnitType.House, 3, 500m, "4975", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.SetUnitSuburbAsync(companyId, "RT-F1", "Mabvuku")).IsSuccess.Should().BeTrue();
        string flat = (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "RT-F1", TenantCustomerId = flatTenant.Id, StartDate = new DateOnly(2026, 4, 1) })).Value;
        (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "RT-H1", TenantCustomerId = houseTenant.Id, StartDate = new DateOnly(2026, 4, 1) })).IsSuccess.Should().BeTrue();
        (await rentals.SetIncomeCategoryAsync(companyId, flat, RentIncomeCategory.Low)).IsSuccess.Should().BeTrue();

        DateOnly june = new(2026, 6, 1);
        (await rentals.SetRentTariffAsync(companyId, RentalUnitType.Flat, null, null, new DateOnly(2026, 6, 15), 320m)).Error.Code.Should().Be("Hse.RentTariff");
        (await rentals.ApplyRentTariffsAsync(companyId, june)).Error.Code.Should().Be("Hse.NoTariffs");
        (await rentals.SetRentTariffAsync(companyId, RentalUnitType.Flat, null, null, june, 310m)).IsSuccess.Should().BeTrue();
        (await rentals.SetRentTariffAsync(companyId, RentalUnitType.Flat, null, null, june, 320m)).IsSuccess.Should().BeTrue("replaces the same tariff");
        (await rentals.SetRentTariffAsync(companyId, RentalUnitType.Flat, "Mabvuku", RentIncomeCategory.Low, june, 250m)).IsSuccess.Should().BeTrue();

        (await rentals.ApplyRentTariffsAsync(companyId, june)).Value.Should().Be(new RentReviewResult(2, 1));
        (await rentals.ApplyRentTariffsAsync(companyId, june)).Value.Should().Be(new RentReviewResult(0, 0));
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 5, 1))).Value.TotalRent.Should().Be(800m);
        (await rentals.RunRentAsync(companyId, june)).Value.TotalRent.Should().Be(750m, "the low-income Mabvuku rate 250 and the house unchanged at 500");
    }
}
