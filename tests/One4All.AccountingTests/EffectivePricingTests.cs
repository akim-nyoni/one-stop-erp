using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers date-effective pricing: a price scheduled in the past is refused; a scheduled price and a scheduled list-wide increase apply from
/// their dates only, so an order dated before keeps the earlier price and one dated after takes the new; the history keeps every price.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class EffectivePricingTests
{
    private readonly AccountingFixture _fixture;

    public EffectivePricingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Scheduled_prices_apply_from_their_date_and_orders_take_the_price_of_their_date()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item widget = new(companyId, "EP-W", "Widget") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Item gadget = new(companyId, "EP-G", "Gadget") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Customer customer = new(companyId, "EP-CUS", "Buyer", Scenario.Usd);
        db.AddRange(widget, gadget, customer);
        await db.SaveChangesAsync();
        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long list = (await pricing.CreatePriceListAsync(companyId, "EP-RET", "Retail", Scenario.Usd, isDefault: true)).Value;
        (await pricing.SetPriceAsync(companyId, list, widget.Id, 1m, 10m)).IsSuccess.Should().BeTrue();
        (await pricing.SetPriceAsync(companyId, list, gadget.Id, 1m, 20m)).IsSuccess.Should().BeTrue();

        DateOnly today = new(2026, 5, 1);
        (await pricing.SchedulePriceAsync(companyId, list, widget.Id, 1m, 12m, today, today)).Error.Code.Should().Be("Price.EffectiveInPast");
        (await pricing.SchedulePriceAsync(companyId, list, widget.Id, 1m, 12m, new DateOnly(2026, 6, 1), today)).IsSuccess.Should().BeTrue();
        // A 10% list-wide increase from 1 July on whatever is in force on 30 June (widget 12, gadget 20).
        (await pricing.ScheduleAdjustmentAsync(companyId, list, 10m, 0.01m, PriceRounding.Nearest, new DateOnly(2026, 7, 1), today)).Value.Should().Be(2);

        async Task<decimal> PriceOn(Item item, DateOnly date) => (await pricing.PriceAsync(companyId, customer.Id, item.Id, 1m, date)).Value.UnitPrice;
        (await PriceOn(widget, new DateOnly(2026, 5, 31)), await PriceOn(widget, new DateOnly(2026, 6, 15)), await PriceOn(widget, new DateOnly(2026, 7, 1))).Should().Be((10m, 12m, 13.2m));
        (await PriceOn(gadget, new DateOnly(2026, 6, 30)), await PriceOn(gadget, new DateOnly(2026, 7, 2))).Should().Be((20m, 22m));

        // Orders take the price in force on their date.
        ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        foreach ((string number, DateOnly date) in new[] { ("EP-SO1", new DateOnly(2026, 5, 20)), ("EP-SO2", new DateOnly(2026, 7, 5)) })
        {
            (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, OrderNumber = number, OrderDate = date, Currency = Scenario.Usd, Lines = [new SalesOrderLineRequest(widget.Id, "Widget", 1m, null, null, "4000")],
            })).IsSuccess.Should().BeTrue();
        }

        (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == companyId).OrderBy(o => o.OrderNumber).Select(o => o.Lines.Single().UnitPrice).ToListAsync())
            .Should().Equal(10m, 13.2m);
        (await pricing.PriceHistoryAsync(companyId, list, widget.Id)).Select(h => (h.UnitPrice, h.EffectiveFrom))
            .Should().Equal((10m, (DateOnly?)null), (12m, new DateOnly(2026, 6, 1)), (13.2m, new DateOnly(2026, 7, 1)));
    }
}
