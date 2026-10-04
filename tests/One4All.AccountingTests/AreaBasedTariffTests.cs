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
/// Covers area-based billing: a roads levy of 5 plus 0.02 per square metre bills an 800 m² stand 21 and a 1,500 m² stand
/// 35 in the fixed-charge run.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AreaBasedTariffTests
{
    private readonly AccountingFixture _fixture;

    public AreaBasedTariffTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_stand_is_billed_on_its_area()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff roads = new(companyId, "ROADS", "Roads Levy", ServiceType.Other, TariffType.AreaBased, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 5m, RatePerSquareMetre = 0.02m };
        db.Tariffs.Add(roads);
        Customer owner = new(companyId, "AB-OWN", "Stand Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        Property small = new(companyId, "A1", "Ward 15", owner.Id) { SizeSquareMetres = 800m };
        small.AddService(ServiceType.Other, roads.Id, new DateOnly(2026, 1, 1));
        Property large = new(companyId, "A2", "Ward 15", owner.Id) { SizeSquareMetres = 1500m };
        large.AddService(ServiceType.Other, roads.Id, new DateOnly(2026, 1, 1));
        db.Properties.AddRange(small, large);
        await db.SaveChangesAsync();

        IMunicipalBillingService billing = scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>();
        MunicipalBillingRunResult run = (await billing.RunFixedChargeBillingAsync(companyId, ServiceType.Other, new DateOnly(2026, 4, 15), "AB")).Value;
        run.PropertiesBilled.Should().Be(2);
        run.TotalBilled.Should().Be(56m);
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"AB-{small.Id}-202604")).OriginalBase.Should().Be(21m);
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"AB-{large.Id}-202604")).OriginalBase.Should().Be(35m);
    }
}
