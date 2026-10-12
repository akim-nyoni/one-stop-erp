using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers carrying rental units as investment property and remeasuring them at fair value.</summary>
[Collection(AccountingCollection.Name)]
public sealed class InvestmentPropertyTests
{
    private readonly AccountingFixture _fixture;

    public InvestmentPropertyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rental_units_are_carried_and_remeasured()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "IP-1", "Block A flat", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "IP-2", "Block B flat", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();

        IRentalPropertyAccountingService accounting = scope.ServiceProvider.GetRequiredService<IRentalPropertyAccountingService>();
        (await accounting.DesignateAsync(companyId, "IP-1", RentalPropertyTreatment.NotDesignated, "1600", 50_000m, "1900", new DateOnly(2026, 1, 1))).Error.Code.Should().Be("Hse.PropertyTreatment");
        (await accounting.DesignateAsync(companyId, "IP-1", RentalPropertyTreatment.InvestmentPropertyFairValue, "1600", 50_000m, "1900", new DateOnly(2026, 1, 1))).Value.Should().NotBeEmpty();
        (await accounting.DesignateAsync(companyId, "IP-1", RentalPropertyTreatment.InvestmentPropertyCost, "1600", 50_000m, "1900", new DateOnly(2026, 1, 2))).Error.Code.Should().Be("Hse.PropertyDesignated");
        (await accounting.DesignateAsync(companyId, "IP-2", RentalPropertyTreatment.InvestmentPropertyCost, "1600", 40_000m, "1900", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();

        (await accounting.RevalueAsync(companyId, "IP-2", 45_000m, new DateOnly(2026, 6, 30), "4900")).Error.Code.Should().Be("Hse.NotFairValue");
        (await accounting.RevalueAsync(companyId, "IP-1", 56_000m, new DateOnly(2026, 6, 30), "4900")).Value.Should().NotBeEmpty();
        (await accounting.RevalueAsync(companyId, "IP-1", 54_000m, new DateOnly(2026, 3, 31), "4900")).Error.Code.Should().Be("Hse.ValuationDate");
        (await accounting.RevalueAsync(companyId, "IP-1", 54_000m, new DateOnly(2026, 12, 31), "4900")).Value.Should().NotBeEmpty();

        IReadOnlyList<RentalPropertyRow> register = await accounting.RegisterAsync(companyId);
        register.Select(r => (r.UnitCode, r.Treatment, r.CarryingAmount)).Should().Equal(
            ("IP-1", RentalPropertyTreatment.InvestmentPropertyFairValue, 54_000m), ("IP-2", RentalPropertyTreatment.InvestmentPropertyCost, 40_000m));
    }
}
