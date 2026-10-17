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
/// Thirteenth audit #1 (brief §4.9): a billing run bills the metered consumption — every accepted reading on the stand's meters for the service that
/// no run has billed yet — and marks those readings billed when the bill posts, so the next run bills only what was read since.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MeteredBillingRunTests
{
    private readonly AccountingFixture _fixture;

    public MeteredBillingRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_billing_run_bills_the_unbilled_meter_readings_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WAT-MB", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 2m, FixedCharge = 5m };
        db.Tariffs.Add(water);
        Customer owner = new(companyId, "MB-OWN", "Metered Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        long meterId = (await meters.InstallAsync(companyId, $"MB-{companyId}", 5, 1m, 100m, new DateOnly(2025, 12, 1))).Value;
        Property stand = new(companyId, "MB1", "Ward 7", owner.Id);
        stand.AddService(ServiceType.Water, water.Id, new DateOnly(2025, 12, 1)).MeterId = meterId;
        db.Properties.Add(stand);
        await db.SaveChangesAsync();
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 1, 28), 120m, false)).Value.BilledConsumption.Should().Be(20m);

        // January's run bills the 20 units read in January: 5 fixed + 20 × 2.
        IBillingRunService runs = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunSummary january = (await runs.ProposeAsync(new BillingRunProposal { CompanyId = companyId, ServiceType = ServiceType.Water, BillDate = new DateOnly(2026, 1, 31), Ward = "Ward 7", InvoicePrefix = "MBW", UserId = 81 })).Value;
        BillingRunLineView line = (await runs.LinesAsync(companyId, january.RunNumber, exceptionsOnly: false)).Value.Single();
        (line.Charge, line.Consumption).Should().Be((45m, 20m));
        (await runs.ApproveAsync(companyId, january.RunNumber, 82)).Value.Posted.Should().Be(1);
        MeterReading read = await db.Set<MeterReading>().AsNoTracking().SingleAsync(r => r.MeterId == meterId && r.ReadingDate == new DateOnly(2026, 1, 28));
        (read.BilledOn, read.BillingRunId is not null).Should().Be((new DateOnly(2026, 1, 31), true), "the reading is marked billed by the run");

        // February has no reading: the fixed charge only. March's reading of 40 units is billed once, by March's run.
        BillingRunSummary february = (await runs.ProposeAsync(new BillingRunProposal { CompanyId = companyId, ServiceType = ServiceType.Water, BillDate = new DateOnly(2026, 2, 28), Ward = "Ward 7", InvoicePrefix = "MBW", UserId = 81 })).Value;
        (await runs.LinesAsync(companyId, february.RunNumber, exceptionsOnly: false)).Value.Single().Charge.Should().Be(5m, "January's reading is not billed twice");
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 3, 28), 160m, false)).IsSuccess.Should().BeTrue();
        BillingRunSummary march = (await runs.ProposeAsync(new BillingRunProposal { CompanyId = companyId, ServiceType = ServiceType.Water, BillDate = new DateOnly(2026, 3, 31), Ward = "Ward 7", InvoicePrefix = "MBW", UserId = 81 })).Value;
        BillingRunLineView marchLine = (await runs.LinesAsync(companyId, march.RunNumber, exceptionsOnly: false)).Value.Single();
        (marchLine.Charge, marchLine.Consumption).Should().Be((85m, 40m));
    }
}
