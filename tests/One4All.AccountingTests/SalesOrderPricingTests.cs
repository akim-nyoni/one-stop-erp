using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers price-list pricing of sales orders: an unpriced line takes the customer's price-list price for
/// its quantity, a keyed price still wins, an unpriced item is refused, and a list in the wrong currency
/// is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesOrderPricingTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderPricingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Unpriced_order_lines_take_the_price_list_price_for_their_quantity()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long widgetId;
        long unpricedId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "SOP-PL", "Priced Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item widget = new(companyId, "SOP-WIDGET", "Widget") { RevenueAccountCode = "4000" };
            Item unpriced = new(companyId, "SOP-NOPRICE", "Unpriced thing") { RevenueAccountCode = "4000" };
            db.Items.AddRange(widget, unpriced);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            widgetId = widget.Id;
            unpricedId = unpriced.Id;

            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
            long listId = (await pricing.CreatePriceListAsync(companyId, "SOPSTD", "Standard", Scenario.Usd, isDefault: true)).Value;
            (await pricing.SetPriceAsync(companyId, listId, widgetId, 1m, 12.00m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, listId, widgetId, 20m, 10.00m)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // Two unpriced lines at different quantities straddle the 20-unit break; a third line keys its own price.
            var order = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOP-PL-1", OrderDate = new DateOnly(2026, 6, 1), Currency = Scenario.Usd,
                Lines =
                [
                    new SalesOrderLineRequest(widgetId, "Widget x5", 5m, null, null, "4000"),
                    new SalesOrderLineRequest(widgetId, "Widget x25", 25m, null, null, "4000"),
                    new SalesOrderLineRequest(widgetId, "Widget special", 1m, 9.99m, null, "4000"),
                ],
            });
            order.IsSuccess.Should().BeTrue(order.IsFailure ? order.Error.ToString() : null);

            List<SalesOrderLine> lines = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == order.Value.SalesOrderId).OrderBy(l => l.Id).ToListAsync();
            lines.Select(l => l.UnitPrice).Should().Equal(12.00m, 10.00m, 9.99m);

            // An item with no price anywhere is refused when no price is keyed.
            var noPrice = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOP-PL-2", OrderDate = new DateOnly(2026, 6, 1), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(unpricedId, "Unpriced", 1m, null, null, "4000")],
            });
            noPrice.Error.Code.Should().Be("Price.NoPrice");

            // ...but a keyed price still works for it.
            (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOP-PL-3", OrderDate = new DateOnly(2026, 6, 1), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(unpricedId, "Unpriced", 1m, 3.00m, null, "4000")],
            })).IsSuccess.Should().BeTrue();

            // A list in another currency cannot price an order in this one.
            var eurOrder = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SOP-PL-4", OrderDate = new DateOnly(2026, 6, 1), Currency = CurrencyCode.Parse("EUR"),
                Lines = [new SalesOrderLineRequest(widgetId, "Widget", 1m, null, null, "4000")],
            });
            eurOrder.Error.Code.Should().Be("Sop.PriceCurrency");
        }
    }
}
