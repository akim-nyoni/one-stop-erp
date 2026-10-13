using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the consumption graph: a stand's water meter read in January (20 units), February (none) and March (40) shows each month of the
/// three to March with its units and an average of 20, and the graph scales its bars to the busiest month.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ConsumptionGraphTests
{
    private readonly AccountingFixture _fixture;

    public ConsumptionGraphTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_stands_meters_show_their_monthly_consumption()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WAT-CG", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        db.Tariffs.Add(water);
        Customer owner = new(companyId, "CG-OWN", "Graph Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        string serial = $"CG-{companyId}";
        long meterId = (await meters.InstallAsync(companyId, serial, 5, 1m, 100m, new DateOnly(2025, 12, 1))).Value;
        Property stand = new(companyId, "CG1", "Ward 3", owner.Id);
        stand.AddService(ServiceType.Water, water.Id, new DateOnly(2025, 12, 1)).MeterId = meterId;
        db.Properties.Add(stand);
        await db.SaveChangesAsync();
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 1, 28), 120m, false)).IsSuccess.Should().BeTrue();
        (await meters.CaptureAsync(companyId, meterId, new DateOnly(2026, 3, 28), 160m, false)).IsSuccess.Should().BeTrue();

        IPropertyRegisterService register = scope.ServiceProvider.GetRequiredService<IPropertyRegisterService>();
        (await register.ConsumptionAsync(companyId, "NOPE", new DateOnly(2026, 3, 31))).Error.Code.Should().Be("Mun.PropertyNotFound");
        MeterConsumption meter = (await register.ConsumptionAsync(companyId, "cg1", new DateOnly(2026, 3, 31), 3)).Value.Single();
        meter.Serial.Should().Be(serial);
        meter.Months.Select(m => (m.Month, m.Units)).Should().Equal((new DateOnly(2026, 1, 1), 20m), (new DateOnly(2026, 2, 1), 0m), (new DateOnly(2026, 3, 1), 40m));
        meter.Average.Should().Be(20m);

        string[] graph = ConsumptionGraph.Render(meter, 72).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        graph[0].Should().Contain(serial).And.Contain("Water");
        graph[1].Count(ch => ch == '#').Should().Be(25, "January is half the busiest month on a 50-character bar");
        graph[2].Should().NotContain("#");
        graph[3].Count(ch => ch == '#').Should().Be(50);
        graph[^1].Should().StartWith("Average").And.EndWith("20");
    }
}
