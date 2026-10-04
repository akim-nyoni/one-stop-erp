using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the tariff catalogue: domestic (council-wide and a Ward 3 version) and commercial water tariffs are offered; a
/// Ward 3 domestic consumer resolves to the Ward 3 tariff, a Ward 9 domestic consumer to the council-wide one, a
/// commercial consumer to the commercial tariff, and an industrial consumer has none.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TariffCatalogueAccountingTests
{
    private readonly AccountingFixture _fixture;

    public TariffCatalogueAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tariffs_resolve_by_category_and_ward()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Tariffs.AddRange(
            new Tariff(companyId, "WAT-DOM", "Domestic Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { Category = ConsumerCategory.Domestic, PerUnitRate = 1m },
            new Tariff(companyId, "WAT-DOM-W3", "Domestic Water Ward 3", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { Category = ConsumerCategory.Domestic, Ward = "Ward 3", PerUnitRate = 0.8m },
            new Tariff(companyId, "WAT-COM", "Commercial Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { Category = ConsumerCategory.Commercial, PerUnitRate = 2m });
        await db.SaveChangesAsync();

        ITariffCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<ITariffCatalogueService>();
        DateOnly date = new(2026, 6, 1);
        (await catalogue.ListAsync(companyId, ServiceType.Water, date)).Should().HaveCount(3);
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Domestic, "Ward 3", date)).Value.Code.Should().Be("WAT-DOM-W3");
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Domestic, "Ward 9", date)).Value.Code.Should().Be("WAT-DOM");
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Commercial, "Ward 3", date)).Value.Code.Should().Be("WAT-COM");
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Industrial, "Ward 3", date)).Error.Code.Should().Be("Mun.NoTariff");
    }
}
