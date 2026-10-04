using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers seasonal billing: water at 1 a unit, 1.5 in the October–March dry season, bills 50 m³ at 75 in January and at
/// 50 in June through the single-service bill.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SeasonalTariffTests
{
    private readonly AccountingFixture _fixture;

    public SeasonalTariffTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Consumption_is_billed_at_the_seasonal_rate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WAT-SEA", "Seasonal Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        water.AddSeason(10, 3, 1.5m);
        db.Tariffs.Add(water);
        Customer owner = new(companyId, "SEA-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "S1", "Ward 16", owner.Id);
        stand.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1));
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        (await billing.BillServiceAsync(companyId, stand.Id, ServiceType.Water, 50m, "SEA-JAN", new DateOnly(2026, 1, 20))).Value.Charge.Should().Be(75m);
        (await billing.BillServiceAsync(companyId, stand.Id, ServiceType.Water, 50m, "SEA-JUN", new DateOnly(2026, 6, 20))).Value.Charge.Should().Be(50m);
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "SEA-JAN")).OriginalBase.Should().Be(75m);
    }
}
