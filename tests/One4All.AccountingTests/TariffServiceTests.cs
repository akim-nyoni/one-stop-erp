using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.System;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers creating and versioning tariffs (brief §4.9): a new code needs its essentials, a later date adds a version that carries over what is not given
/// (blocks and seasons too), the catalogue resolves the version in force on a date, a withdrawn version steps aside, and an approved tariff change
/// becomes a new version from the day it is applied instead of overwriting the version that raised earlier bills.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TariffServiceTests
{
    private readonly AccountingFixture _fixture;

    public TariffServiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tariffs_are_versioned_not_overwritten()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IMunicipalTariffService tariffs = scope.ServiceProvider.GetRequiredService<IMunicipalTariffService>();

        (await tariffs.SaveVersionAsync(companyId, new TariffInput("TS-WAT", new DateOnly(2026, 1, 1)))).Error.Code.Should().Be("Mun.TariffInvalid", "a new code needs its essentials");
        (await tariffs.SaveVersionAsync(companyId, new TariffInput("TS-WAT", new DateOnly(2026, 1, 1), "Water", ServiceType.Water, TariffType.Stepped, "9999"))).Error.Code.Should().Be("Mun.AccountNotFound");
        var created = await tariffs.SaveVersionAsync(companyId, new TariffInput("ts-wat", new DateOnly(2026, 1, 1), "Water (stepped)", ServiceType.Water, TariffType.Stepped, "4000", FixedCharge: 5m,
            Blocks: [new TariffBlockInput(0m, 10m, 0.5m), new TariffBlockInput(10m, null, 1.2m)], Seasons: [new TariffSeasonInput(5, 8, 0.9m)]));
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : null);
        (await tariffs.SaveVersionAsync(companyId, new TariffInput("TS-WAT", new DateOnly(2026, 1, 1), FixedCharge: 6m))).Error.Code.Should().Be("Mun.TariffVersionExists");

        // A July version: only the fixed charge changes; the blocks, season and account carry over.
        var july = await tariffs.SaveVersionAsync(companyId, new TariffInput("TS-WAT", new DateOnly(2026, 7, 1), FixedCharge: 7m));
        july.IsSuccess.Should().BeTrue(july.IsFailure ? july.Error.Message : null);
        Tariff second = await db.Tariffs.AsNoTracking().Include(t => t.Blocks).Include(t => t.Seasons).SingleAsync(t => t.Id == july.Value);
        (second.Code, second.Name, second.ServiceType, second.TariffType, second.RevenueAccountCode, second.FixedCharge, second.Blocks.Count, second.Seasons.Count)
            .Should().Be(("TS-WAT", "Water (stepped)", ServiceType.Water, TariffType.Stepped, "4000", 7m, 2, 1));
        second.CalculateCharge(20m, 0m).Should().Be(7m + 10m * 0.5m + 10m * 1.2m);

        ITariffCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<ITariffCatalogueService>();
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Domestic, null, new DateOnly(2026, 6, 30))).Value.TariffId.Should().Be(created.Value);
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Domestic, null, new DateOnly(2026, 7, 1))).Value.TariffId.Should().Be(july.Value);
        (await tariffs.WithdrawVersionAsync(companyId, "TS-WAT", new DateOnly(2026, 7, 1))).IsSuccess.Should().BeTrue();
        (await catalogue.ResolveAsync(companyId, ServiceType.Water, ConsumerCategory.Domestic, null, new DateOnly(2026, 8, 1))).Value.TariffId.Should().Be(created.Value, "the withdrawn version steps aside");
        (await tariffs.WithdrawVersionAsync(companyId, "TS-WAT", new DateOnly(2030, 1, 1))).Error.Code.Should().Be("Mun.TariffVersionNotFound");

        // An approved change to the version in force becomes a new version from today; the January version keeps its charge.
        IMasterDataChangeService changes = scope.ServiceProvider.GetRequiredService<IMasterDataChangeService>();
        MasterDataChangeRow applied = (await changes.RequestAsync(companyId, MasterDataTarget.Tariff, "TS-WAT", "FixedCharge", "9", userId: 1)).Value;
        applied.Status.Should().Be(MasterDataChangeStatus.Applied);
        List<Tariff> versions = await db.Tariffs.AsNoTracking().Where(t => t.CompanyId == companyId && t.Code == "TS-WAT").OrderBy(t => t.EffectiveFrom).ToListAsync();
        versions.Should().HaveCount(3);
        (versions[0].FixedCharge, versions[2].FixedCharge, versions[2].EffectiveFrom).Should().Be((5m, 9m, DateOnly.FromDateTime(DateTime.UtcNow)));
        (await db.Tariffs.AsNoTracking().Include(t => t.Blocks).SingleAsync(t => t.Id == versions[2].Id)).Blocks.Should().HaveCount(2, "the new version keeps the blocks");
    }
}
