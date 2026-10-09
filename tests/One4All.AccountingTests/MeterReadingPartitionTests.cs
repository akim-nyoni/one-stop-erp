using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers meter readings partitioned by year (brief §11): the table is partitioned, a reading lands in its year's partition and still reads and updates as one table.</summary>
[Collection(AccountingCollection.Name)]
public sealed class MeterReadingPartitionTests
{
    private readonly AccountingFixture _fixture;

    public MeterReadingPartitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Readings_land_in_their_year_partition()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Meter meter = new(companyId, $"MP-{companyId}");
        db.Set<Meter>().Add(meter);
        await db.SaveChangesAsync();

        MeterReading reading = new(companyId, meter.Id, new DateOnly(2026, 4, 30), 1250m, 1000m, 250m);
        db.Set<MeterReading>().Add(reading);
        await db.SaveChangesAsync();
        reading.Id.Should().BeGreaterThan(0);

        (await db.Database.SqlQuery<string>($"SELECT relkind::text AS \"Value\" FROM pg_class WHERE oid = 'mun.meter_reading'::regclass").SingleAsync()).Should().Be("p");
        (await db.Database.SqlQuery<string>($"SELECT tableoid::regclass::text AS \"Value\" FROM mun.meter_reading WHERE id = {reading.Id}").SingleAsync())
            .Should().Be("mun.meter_reading_y2026");
        (await db.Set<MeterReading>().AsNoTracking().SingleAsync(r => r.Id == reading.Id)).Reading.Should().Be(1250m);
    }
}
