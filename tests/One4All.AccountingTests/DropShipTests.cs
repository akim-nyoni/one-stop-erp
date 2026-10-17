using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers drop-shipment: the supplier sends order lines straight to the customer - never more than is left to send - and its confirmed
/// delivery puts the supplier's invoice to cost of sales and makes the lines invoiceable, with no stock movement.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DropShipTests
{
    private readonly AccountingFixture _fixture;

    public DropShipTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Suppliers_deliver_straight_to_the_customer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DS-C", "Farm Co-op", Scenario.Usd);
        Supplier supplier = new(companyId, "DS-S", "Tractor Parts Ltd", Scenario.Usd);
        Item part = new(companyId, "DS-PART", "Gearbox") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        db.AddRange(customer, supplier, part);
        await db.SaveChangesAsync();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "DS-SO-1", OrderDate = new DateOnly(2026, 5, 1), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(part.Id, "Gearbox", 10m, 50m, null, "4000")],
        })).Value.SalesOrderId;
        long lineId = (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId)).Lines[0].Id;

        IDropShipService dropShip = scope.ServiceProvider.GetRequiredService<IDropShipService>();
        (await dropShip.OrderAsync(companyId, orderId, supplier.Id, "DS-1", new DateOnly(2026, 5, 2), [new DropShipLine(lineId, 6m, 30m)])).IsSuccess.Should().BeTrue();
        (await dropShip.OrderAsync(companyId, orderId, supplier.Id, "DS-2", new DateOnly(2026, 5, 2), [new DropShipLine(lineId, 5m, 30m)])).Error.Code.Should().Be("Sop.DropShipExceeded");

        DropShipDelivered delivered = (await dropShip.ConfirmDeliveredAsync(companyId, "DS-1", new DateOnly(2026, 5, 6), "TP-INV-77", "5000")).Value;
        delivered.Cost.Should().Be(180m);
        (await dropShip.ConfirmDeliveredAsync(companyId, "DS-1", new DateOnly(2026, 5, 6), "TP-INV-78", "5000")).Error.Code.Should().Be("Sop.DropShipDelivered");
        (await db.SalesDeliveries.AsNoTracking().CountAsync(d => d.CompanyId == companyId && d.Reference == "DS-1")).Should().Be(1);
        (await db.StockMovements.AsNoTracking().CountAsync(m => m.CompanyId == companyId && m.ItemId == part.Id)).Should().Be(0);

        // The six delivered gearboxes are invoiced to the customer as usual.
        var invoice = await orders.InvoiceAsync(new InvoiceOrderRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, InvoiceNumber = "DS-INV-1", Date = new DateOnly(2026, 5, 7), Lines = [new InvoiceOrderLine(lineId, 6m, 0m)],
        });
        invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
        invoice.Value.InvoiceValue.Should().Be(300m);

        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 5, 31));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Where(r => r.AccountCode == "5000").Sum(r => r.Debit - r.Credit).Should().Be(180m);
        tb.Rows.Where(r => r.AccountCode == "4000").Sum(r => r.Credit - r.Debit).Should().Be(300m);
    }
}
