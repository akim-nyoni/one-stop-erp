using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the meter register import: each meter goes on the register with its opening reading and is linked to the property's service; a stand
/// without the service, a service already metered or a serial already on the register fails the file before anything is written.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MeterRegisterImportTests
{
    private readonly AccountingFixture _fixture;

    public MeterRegisterImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Meters_are_registered_with_their_opening_readings_and_linked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Customers.Add(new Customer(companyId, "MR-OWN", "Ratepayer", Scenario.Usd));
        db.Tariffs.Add(new Tariff(companyId, "MR-WAT", "Water", ServiceType.Water, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 10m });
        await db.SaveChangesAsync();
        IVerticalImportService import = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();
        (await import.ImportPropertiesAsync(companyId, "stand,ward,ratepayer,services\nMR-S1,Ward 1,MR-OWN,Water:MR-WAT\nMR-S2,Ward 1,MR-OWN,Water:MR-WAT\n", commit: true))
            .Created.Should().Be(2);

        const string bad = "serial,stand,service,reading,date,digits\nM-001,MR-S1,Water,1234,2026-06-30,6\nM-002,MR-S1,Water,10,2026-06-30,6\nM-003,MR-S9,Water,0,2026-06-30,6\nM-004,MR-S2,Sewer,0,2026-06-30,6\n";
        ImportReport refused = await import.ImportMetersAsync(companyId, bad, commit: true);
        (refused.Committed, refused.Errors.Count).Should().Be((false, 3));     // MR-S1's water twice, no stand MR-S9, no sewer on MR-S2
        (await db.Set<Meter>().CountAsync(m => m.CompanyId == companyId)).Should().Be(0);

        ImportReport loaded = await import.ImportMetersAsync(companyId, "serial,stand,service,reading,date,digits\nm-001,MR-S1,water,1234,2026-06-30,6\nM-002,MR-S2,Water,77,2026-06-30,6\n", commit: true);
        (loaded.Committed, loaded.Created).Should().Be((true, 2));
        Meter first = await db.Set<Meter>().AsNoTracking().SingleAsync(m => m.CompanyId == companyId && m.Serial == "M-001");
        (first.LastActualReading, first.Digits, first.LastActualDate).Should().Be((1234m, 6, (DateOnly?)new DateOnly(2026, 6, 30)));
        (await db.Set<PropertyServiceLink>().AsNoTracking().CountAsync(l => l.MeterId == first.Id)).Should().Be(1);
        (await db.Set<MeterReading>().AsNoTracking().CountAsync(r => r.MeterId == first.Id && r.Accepted)).Should().Be(1);
        (await import.ImportMetersAsync(companyId, "serial,stand,service,reading,date\nM-009,MR-S1,Water,0,2026-07-01\n", commit: true)).Errors.Should().ContainSingle();
    }
}
