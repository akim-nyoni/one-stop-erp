using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers routing fired tickets to their station printers: unmapped stations stay on the display, a dead printer is reported, and settings are checked.</summary>
[Collection(AccountingCollection.Name)]
public sealed class KitchenStationPrintTests
{
    private readonly AccountingFixture _fixture;

    public KitchenStationPrintTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Fired_tickets_go_to_their_station_printers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        TableOrder order = new(companyId, "KSP-ORD-1", OrderType.Takeaway, 2, new DateOnly(2026, 3, 1), "Walk-in");
        db.Set<TableOrder>().Add(order);
        await db.SaveChangesAsync();
        KitchenTicket grill = new(companyId, order.Id, "KSP-1-GRILL", new DateOnly(2026, 3, 1), null) { Station = "Grill" };
        grill.AddLine(1, "Rump steak", 2m);
        KitchenTicket bar = new(companyId, order.Id, "KSP-1-BAR", new DateOnly(2026, 3, 1), null) { Station = "Bar" };
        bar.AddLine(2, "Lager", 3m);
        db.Set<KitchenTicket>().AddRange(grill, bar);
        await db.SaveChangesAsync();

        IKitchenPrintService kitchen = scope.ServiceProvider.GetRequiredService<IKitchenPrintService>();
        (await kitchen.SetStationPrinterAsync(companyId, new StationPrinterSetting("grill", "Kitchen", "10.0.0.5"))).Error.Code.Should().Be("Res.StationPrinter");
        (await kitchen.SetStationPrinterAsync(companyId, new StationPrinterSetting("grill", null, "127.0.0.1", 1))).IsSuccess.Should().BeTrue();
        (await kitchen.StationPrintersAsync(companyId)).Should().ContainSingle().Which.Station.Should().Be("GRILL");

        IReadOnlyList<KitchenPrintOutcome> outcomes = (await kitchen.PrintAsync(companyId, "KSP-1")).Value;
        outcomes.Select(o => o.Station).Should().Equal("BAR", "GRILL");
        outcomes[0].Printer.Should().BeNull();
        outcomes[0].Text.Should().Contain("3 x Lager").And.Contain("Order KSP-ORD-1").And.Contain("Takeaway - Walk-in");
        outcomes[1].Printer.Should().Be("127.0.0.1:1");
        outcomes[1].Error.Should().NotBeNull();
        outcomes[1].Printed.Should().BeFalse();
        (await db.Set<KitchenTicket>().AsNoTracking().SingleAsync(t => t.Id == grill.Id)).PrintCount.Should().Be(0);

        (await kitchen.PrintAsync(companyId, "NOPE")).Error.Code.Should().Be("Res.TicketNotFound");
        (await kitchen.SetStationPrinterAsync(companyId, new StationPrinterSetting("GRILL", null, null), remove: true)).IsSuccess.Should().BeTrue();
        (await kitchen.StationPrintersAsync(companyId)).Should().BeEmpty();
    }
}
