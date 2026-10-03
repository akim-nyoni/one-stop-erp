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
/// Covers meter readings on a four-dial water meter: a high reading is flagged; a missed month is estimated at the
/// historical rate (50); the next actual reading below the estimate bills nothing and credits 20 units, which the next
/// reading nets off; a tampered and a negative reading are rejected; a wrap past 9999 is a rollover; replacing the meter
/// takes its final reading, moves the water service to the new meter and retires the old one; the exceptions report
/// lists every flagged reading.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MeterReadingTests
{
    private readonly AccountingFixture _fixture;

    public MeterReadingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Readings_are_validated_estimated_and_carried_over_on_replacement()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WATER", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        db.Tariffs.Add(water);
        Customer owner = new(companyId, "MR-OWN", "Meter Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        string serial = $"W-{companyId}";
        long meterId = (await meters.InstallAsync(companyId, serial, 4, 1m, 9000m, new DateOnly(2026, 1, 1))).Value;
        (await meters.InstallAsync(companyId, serial, 4, 1m, 0m, new DateOnly(2026, 1, 1))).Error.Code.Should().Be("Mun.SerialInUse");
        Property stand = new(companyId, "501", "Ward 6", owner.Id);
        stand.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1)).MeterId = meterId;
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        async Task<ReadingOutcome> Read(int month, int day, decimal reading, bool tamper = false)
            => (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, month, day), reading, tamper)).Value;

        (await Read(1, 31, 9030m)).BilledConsumption.Should().Be(30m);
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 1, 31), 9040m, false)).Error.Code.Should().Be("Mun.ReadingDate");
        ReadingOutcome high = await Read(3, 2, 9100m);
        high.Flags.Should().Be(ReadingFlags.HighConsumption);
        high.BilledConsumption.Should().Be(70m);

        ReadingOutcome estimate = (await meters.EstimateAsync(companyId, meterId, new DateOnly(2026, 4, 1))).Value;
        estimate.Reading.Should().Be(9150m);
        estimate.BilledConsumption.Should().Be(50m);

        ReadingOutcome corrected = await Read(5, 1, 9130m);
        corrected.Flags.Should().Be(ReadingFlags.LowConsumption | ReadingFlags.EstimateCorrection);
        corrected.BilledConsumption.Should().Be(0m);
        (await db.Set<Meter>().AsNoTracking().SingleAsync(m => m.Id == meterId)).UnitsCredit.Should().Be(20m);

        ReadingOutcome tampered = await Read(5, 31, 9160m, tamper: true);
        tampered.Accepted.Should().BeFalse();
        tampered.Flags.Should().Be(ReadingFlags.Tamper);
        (await Read(6, 1, 9100m)).Should().Match<ReadingOutcome>(r => !r.Accepted && r.Flags == ReadingFlags.Negative);
        (await Read(6, 2, 9180m)).BilledConsumption.Should().Be(30m);

        ReadingOutcome rollover = await Read(7, 2, 40m);
        rollover.Flags.Should().Be(ReadingFlags.Rollover | ReadingFlags.HighConsumption);
        rollover.BilledConsumption.Should().Be(860m);

        DateOnly swap = new(2026, 7, 10);
        (await meters.ReplaceAsync(companyId, meterId, swap, 50m, serial, 5, 1m, 0m)).Error.Code.Should().Be("Mun.SerialInUse");
        MeterReplacement replaced = (await meters.ReplaceAsync(companyId, meterId, swap, 50m, serial + "-N", 5, 1m, 0m)).Value;
        replaced.FinalReading.BilledConsumption.Should().Be(10m);
        replaced.ServicesRelinked.Should().Be(1);
        (await db.Set<PropertyServiceLink>().AsNoTracking().SingleAsync(l => l.PropertyId == stand.Id)).MeterId.Should().Be(replaced.NewMeterId);
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 8, 1), 60m, false)).Error.Code.Should().Be("Mun.MeterInactive");
        (await meters.CaptureAsync(companyId, replaced.NewMeterId, new DateOnly(2026, 8, 9), 35m, false)).Value.BilledConsumption.Should().Be(35m);

        (await meters.ExceptionsAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).Select(e => (e.ReadingDate, e.Flags, e.Accepted)).Should().Equal(
            (new DateOnly(2026, 3, 2), ReadingFlags.HighConsumption, true),
            (new DateOnly(2026, 5, 1), ReadingFlags.LowConsumption | ReadingFlags.EstimateCorrection, true),
            (new DateOnly(2026, 5, 31), ReadingFlags.Tamper, false),
            (new DateOnly(2026, 6, 1), ReadingFlags.Negative, false),
            (new DateOnly(2026, 7, 2), ReadingFlags.Rollover | ReadingFlags.HighConsumption, true),
            (new DateOnly(2026, 7, 10), ReadingFlags.LowConsumption, true));
        (await meters.HistoryAsync(companyId, meterId)).Should().HaveCount(10);
    }
}
