using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Audit #22: a price per tonne typed on a weighbridge invoice that differs from the load's order (or price book) price stands only on the word
/// of an authoriser other than the user raising the invoice, holding Inventory.Weighbridge override price; who authorised it is kept on the ticket.
/// The order's own price needs no authoriser.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WeighbridgePriceOverrideTests
{
    // A user id no user created by the test can be given, so the authoriser is never the clerk by accident.
    private const long Clerk = 900_031;

    private readonly AccountingFixture _fixture;

    public WeighbridgePriceOverrideTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_typed_price_other_than_the_orders_needs_an_authoriser_recorded_on_the_ticket()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer builder = new(companyId, "WP-B", "Price Builder", Scenario.Usd);
        Item sand = new(companyId, "WP-SAND", "River sand") { RevenueAccountCode = "4000" };
        db.AddRange(builder, sand, new WeighbridgeVehicle(companyId, "WP-1") { StoredTareKg = 9_000m });
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISalesOrderService>().CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = builder.Id, OrderNumber = "WP-SO1", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(sand.Id, "Load", 60m, 12m, null, "4000")],
        })).IsSuccess.Should().BeTrue();

        IWeighbridgeService bridge = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        async Task<long> Load()
        {
            (long ticket, _) = (await bridge.FirstWeighAsync(new FirstWeighRequest
            {
                CompanyId = companyId, Direction = WeighDirection.Outbound, Vehicle = "WP-1", ItemId = sand.Id, CubicMetresPerTonne = 0.6m, CustomerId = builder.Id,
                Date = new DateOnly(2026, 3, 10), OperatorUserId = Clerk, SalesOrderNumber = "WP-SO1",
            })).Value;
            (await bridge.SecondWeighAsync(new SecondWeighRequest { CompanyId = companyId, TicketId = ticket, WeightKg = 19_000m, OperatorUserId = Clerk })).IsSuccess.Should().BeTrue();
            return ticket;
        }

        WeighbridgeInvoiceRequest Invoice(string number, decimal price, long? authorisedBy) => new()
        {
            CompanyId = companyId, CustomerId = builder.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 31), PricePerTonne = price, SalesAccountCode = "4000",
            EnteredByUserId = Clerk, PriceOverrideAuthorisedByUserId = authorisedBy,
        };
        long authoriser = await Scenario.UserWithAsync(db, "wp-authoriser", "Inventory.Weighbridge", Core.Security.OperationFlags.OverridePrice);
        long viewer = await Scenario.UserWithAsync(db, "wp-viewer", "Inventory.Weighbridge", Core.Security.OperationFlags.View);

        // 15 a tonne against the order's 12: refused without an authoriser, by the clerk themself, or by someone without the right.
        long first = await Load();
        (await bridge.InvoiceTicketsAsync(Invoice("WP-INV0", 15m, null))).Error.Code.Should().Be("Wb.PriceOverride");
        (await bridge.InvoiceTicketsAsync(Invoice("WP-INV0", 15m, Clerk))).Error.Code.Should().Be("Wb.PriceOverride");
        (await bridge.InvoiceTicketsAsync(Invoice("WP-INV0", 15m, viewer))).Error.Code.Should().Be("Wb.PriceOverride");
        (await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == first)).InvoiceNumber.Should().BeNull("nothing was billed");

        WeighbridgeInvoice billed = (await bridge.InvoiceTicketsAsync(Invoice("WP-INV1", 15m, authoriser))).Value;
        billed.Product.Should().Be(10m * 15m);
        WeighbridgeTicket overridden = await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == first);
        (overridden.InvoiceNumber, overridden.PriceOverridePerTonne, overridden.PriceOverrideAuthorisedByUserId).Should().Be(("WP-INV1", (decimal?)15m, (long?)authoriser));

        // The order's own price needs no authoriser and records no override.
        long second = await Load();
        (await bridge.InvoiceTicketsAsync(Invoice("WP-INV2", 12m, null))).Value.Product.Should().Be(10m * 12m);
        WeighbridgeTicket agreed = await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == second);
        (agreed.InvoiceNumber, agreed.PriceOverridePerTonne, agreed.PriceOverrideAuthorisedByUserId).Should().Be(("WP-INV2", (decimal?)null, (long?)null));
    }
}
