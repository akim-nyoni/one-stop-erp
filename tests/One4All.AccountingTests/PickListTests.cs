using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the pick list: only undespatched quantity is listed, on-hand is the picking warehouse's (not
/// company-wide), shortages are flagged, and cancelled / unknown orders are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PickListTests
{
    private readonly AccountingFixture _fixture;

    public PickListTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pick_list_lists_outstanding_lines_against_the_picking_warehouse()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long widgetId;
        long gadgetId;
        long mainId;
        long otherId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "PK-C", "Pick Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item widget = new(companyId, "PK-WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item gadget = new(companyId, "PK-GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(widget, gadget);
            Warehouse main = new(companyId, "PKMAIN", "Main");
            Warehouse other = new(companyId, "PKOTHR", "Other");
            db.Warehouses.AddRange(main, other);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            widgetId = widget.Id;
            gadgetId = gadget.Id;
            mainId = main.Id;
            otherId = other.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Main: 20 widgets, 3 gadgets. Other: 50 gadgets (must not count for a Main pick).
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = widgetId, WarehouseId = mainId, Date = new DateOnly(2026, 3, 1), Quantity = 20m, UnitCost = 1m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = gadgetId, WarehouseId = mainId, Date = new DateOnly(2026, 3, 1), Quantity = 3m, UnitCost = 2m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = gadgetId, WarehouseId = otherId, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 2m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            var order = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "PK-SO1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines =
                [
                    new SalesOrderLineRequest(widgetId, "Widget", 12m, 5m, null, "4000"),
                    new SalesOrderLineRequest(gadgetId, "Gadget", 8m, 9m, null, "4000"),
                ],
            });
            order.IsSuccess.Should().BeTrue(order.IsFailure ? order.Error.ToString() : null);
            orderId = order.Value.SalesOrderId;

            // Despatch 4 widgets: the pick list should show the remaining 8.
            long widgetLineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == orderId && l.ItemId == widgetId).Select(l => l.Id).SingleAsync();
            (await sop.DespatchAsync(new DespatchRequest { CompanyId = companyId, SalesOrderId = orderId, WarehouseId = mainId, Date = new DateOnly(2026, 3, 3), Reference = "PK-DN1", Lines = [new DespatchLine(widgetLineId, 4m)] })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPickListService picks = scope.ServiceProvider.GetRequiredService<IPickListService>();

            var built = await picks.BuildAsync(companyId, orderId, mainId);
            built.IsSuccess.Should().BeTrue(built.IsFailure ? built.Error.ToString() : null);
            PickListDocument doc = built.Value;
            doc.OrderNumber.Should().Be("PK-SO1");
            doc.CustomerCode.Should().Be("PK-C");
            doc.WarehouseCode.Should().Be("PKMAIN");
            doc.Lines.Should().HaveCount(2);

            PickListLine widget = doc.Lines.Single(l => l.ItemCode == "PK-WIDGET");
            widget.ToPick.Should().Be(8m); // 12 ordered − 4 despatched
            widget.OnHand.Should().Be(16m); // 20 − 4 issued
            widget.Short.Should().Be(0m);

            PickListLine gadget = doc.Lines.Single(l => l.ItemCode == "PK-GADGET");
            gadget.ToPick.Should().Be(8m);
            gadget.OnHand.Should().Be(3m); // Main only; Other's 50 is irrelevant here
            gadget.Short.Should().Be(5m);
            doc.IsComplete.Should().BeFalse();

            // From the other warehouse the gadget is fine but there are no widgets at all.
            PickListDocument fromOther = (await picks.BuildAsync(companyId, orderId, otherId)).Value;
            fromOther.Lines.Single(l => l.ItemCode == "PK-GADGET").Short.Should().Be(0m);
            fromOther.Lines.Single(l => l.ItemCode == "PK-WIDGET").Short.Should().Be(8m);

            string text = (await picks.RenderTextAsync(companyId, orderId, mainId)).Value;
            text.Should().Contain("PICK LIST").And.Contain("PK-SO1").And.Contain("PK-GADGET Gadget").And.Contain("SHORT 5.00").And.Contain("PART PICK");

            (await picks.BuildAsync(companyId, 999_999, mainId)).Error.Code.Should().Be("Sop.OrderNotFound");
            (await picks.BuildAsync(companyId, orderId, 999_999)).Error.Code.Should().Be("Inv.WarehouseNotFound");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // A cancelled order has nothing to pick. (This order has a despatch, so cancel a fresh one.)
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            IPickListService picks = scope.ServiceProvider.GetRequiredService<IPickListService>();
            long freshId = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "PK-SO2", OrderDate = new DateOnly(2026, 3, 4), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(widgetId, "Widget", 1m, 5m, null, "4000")],
            })).Value.SalesOrderId;
            (await sop.CancelOrderAsync(companyId, freshId, "test")).IsSuccess.Should().BeTrue();
            (await picks.BuildAsync(companyId, freshId, mainId)).Error.Code.Should().Be("Sop.Cancelled");
        }
    }
}
