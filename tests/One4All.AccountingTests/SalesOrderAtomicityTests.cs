using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sales-order despatch and invoicing as single units of work: an invoice credits each line's own revenue
/// account; an invoice refused because the customer is on hold leaves the order untouched; over-despatch is refused;
/// and a despatch whose second line cannot be issued issues nothing at all.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesOrderAtomicityTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderAtomicityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Despatch_and_invoice_stand_or_fall_together()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long warehouseId;
        long serviceA;
        long serviceB;
        long stocked;
        long scarce;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "SOA-C", "Order Customer", Scenario.Usd);
            Item a = new(companyId, "SOA-SVC-A", "Installation") { RevenueAccountCode = "4000" };
            Item b = new(companyId, "SOA-SVC-B", "Contract work") { RevenueAccountCode = "4300" };
            Item s = new(companyId, "SOA-STOCK", "Stocked part") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
            Item x = new(companyId, "SOA-SCARCE", "Scarce part") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "SOAWH", "Main");
            db.AddRange(customer, a, b, s, x, wh);
            await db.SaveChangesAsync();
            (customerId, warehouseId, serviceA, serviceB, stocked, scarce) = (customer.Id, wh.Id, a.Id, b.Id, s.Id, x.Id);

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = stocked, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 2m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = scarce, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 1m, UnitCost = 3m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        long serviceOrder;
        long stockOrder;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            serviceOrder = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOA-1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(serviceA, "Installation", 2m, 10m, null, "4000"), new SalesOrderLineRequest(serviceB, "Contract work", 1m, 50m, null, "4300")],
            })).Value.SalesOrderId;
            stockOrder = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOA-2", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(stocked, "Stocked part", 5m, 8m, null, "4000"), new SalesOrderLineRequest(scarce, "Scarce part", 3m, 9m, null, "4000")],
            })).Value.SalesOrderId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<SalesOrderLine> serviceLines = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == serviceOrder).OrderBy(l => l.Id).ToListAsync();
            List<SalesOrderLine> stockLines = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == stockOrder).OrderBy(l => l.Id).ToListAsync();

            // Over-despatch is refused.
            (await sop.DespatchAsync(new DespatchRequest { CompanyId = companyId, SalesOrderId = serviceOrder, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Reference = "DN-X", Lines = [new DespatchLine(serviceLines[0].Id, 3m)] }))
                .Error.Code.Should().Be("Sop.OverDespatch");

            // The scarce line cannot be issued (1 on hand, 3 wanted): nothing is issued, not even the stocked line.
            (await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = stockOrder, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Reference = "DN-2",
                Lines = [new DespatchLine(stockLines[0].Id, 5m), new DespatchLine(stockLines[1].Id, 3m)],
            })).Error.Code.Should().Be("Inv.InsufficientStock");

            // The service order despatches in full.
            (await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = serviceOrder, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Reference = "DN-1",
                Lines = [new DespatchLine(serviceLines[0].Id, 2m), new DespatchLine(serviceLines[1].Id, 1m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == stocked)).QuantityOnHand.Should().Be(10m);
            (await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == stockOrder).SumAsync(l => l.DespatchedQuantity)).Should().Be(0m);

            // On hold: the invoice is refused and, even after a save in this scope, no line is marked invoiced.
            Customer customer = await db.Customers.SingleAsync(c => c.Id == customerId);
            customer.OnHold = true;
            await db.SaveChangesAsync();

            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            List<SalesOrderLine> lines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == serviceOrder).OrderBy(l => l.Id).ToListAsync();
            InvoiceOrderRequest request = new()
            {
                CompanyId = companyId, SalesOrderId = serviceOrder, InvoiceNumber = "SOA-INV", Date = new DateOnly(2026, 3, 6), TaxAccountCode = "2200",
                Lines = [new InvoiceOrderLine(lines[0].Id, 2m, 0m), new InvoiceOrderLine(lines[1].Id, 1m, 0m)],
            };
            (await sop.InvoiceAsync(request)).Error.Code.Should().Be("Ar.CustomerOnHold");
            await db.SaveChangesAsync();
            (await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == serviceOrder).SumAsync(l => l.InvoicedQuantity)).Should().Be(0m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Customers.SingleAsync(c => c.Id == customerId)).OnHold = false;
            await db.SaveChangesAsync();

            // Released: the invoice credits 20 to 4000 and 50 to 4300.
            List<SalesOrderLine> lines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == serviceOrder).OrderBy(l => l.Id).ToListAsync();
            var invoice = await scope.ServiceProvider.GetRequiredService<ISalesOrderService>().InvoiceAsync(new InvoiceOrderRequest
            {
                CompanyId = companyId, SalesOrderId = serviceOrder, InvoiceNumber = "SOA-INV", Date = new DateOnly(2026, 3, 6), TaxAccountCode = "2200",
                Lines = [new InvoiceOrderLine(lines[0].Id, 2m, 0m), new InvoiceOrderLine(lines[1].Id, 1m, 0m)],
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(20m);
            tb.Rows.Single(r => r.AccountCode == "4300").Credit.Should().Be(50m);
            (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
        }
    }
}
