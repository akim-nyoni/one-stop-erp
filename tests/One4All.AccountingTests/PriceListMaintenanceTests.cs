using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bulk price-list maintenance: a percentage increase rounded to a price point across every break,
/// an increase limited to chosen items, and copying a list at a discount to a new list that a customer can
/// then be priced from.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PriceListMaintenanceTests
{
    private readonly AccountingFixture _fixture;

    public PriceListMaintenanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prices_adjust_in_bulk_and_lists_copy_at_a_discount()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long widgetId;
        long gadgetId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item widget = new(companyId, "PM-WIDGET", "Widget") { RevenueAccountCode = "4000" };
            Item gadget = new(companyId, "PM-GADGET", "Gadget") { RevenueAccountCode = "4000" };
            db.Items.AddRange(widget, gadget);
            Customer customer = new(companyId, "PM-TRADE", "Trade Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            widgetId = widget.Id;
            gadgetId = gadget.Id;
            customerId = customer.Id;
        }

        long standardId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
            standardId = (await pricing.CreatePriceListAsync(companyId, "PMSTD", "Standard", Scenario.Usd, isDefault: true)).Value;
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 1m, 9.99m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 10m, 8.99m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, standardId, gadgetId, 1m, 20.00m)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

            // +5% on widgets only, rounded up to 0.05: 9.99 → 10.49 → 10.50; 8.99 → 9.4395 → 9.45. Gadget untouched.
            var adjusted = await pricing.AdjustPricesAsync(companyId, standardId, 5m, 0.05m, PriceRounding.Up, [widgetId]);
            adjusted.IsSuccess.Should().BeTrue(adjusted.IsFailure ? adjusted.Error.ToString() : null);
            adjusted.Value.Should().Be(2);

            (await pricing.PriceAsync(companyId, null, widgetId, 1m)).Value.UnitPrice.Should().Be(10.50m);
            (await pricing.PriceAsync(companyId, null, widgetId, 10m)).Value.UnitPrice.Should().Be(9.45m);
            (await pricing.PriceAsync(companyId, null, gadgetId, 1m)).Value.UnitPrice.Should().Be(20.00m);

            // Copy standard to a trade list at −10%, nearest 0.10.
            var trade = await pricing.CopyPriceListAsync(companyId, standardId, "PMTRADE", "Trade", -10m, 0.10m, PriceRounding.Nearest);
            trade.IsSuccess.Should().BeTrue(trade.IsFailure ? trade.Error.ToString() : null);
            (await pricing.AssignCustomerAsync(companyId, customerId, trade.Value)).IsSuccess.Should().BeTrue();

            (await pricing.PriceAsync(companyId, customerId, widgetId, 1m)).Value.Should().BeEquivalentTo(new { UnitPrice = 9.50m, FromCustomerList = true });  // 10.50 × 0.9 = 9.45 → 9.50
            (await pricing.PriceAsync(companyId, customerId, widgetId, 12m)).Value.UnitPrice.Should().Be(8.50m); // 9.45 × 0.9 = 8.505 → 8.50
            (await pricing.PriceAsync(companyId, customerId, gadgetId, 1m)).Value.UnitPrice.Should().Be(18.00m);

            // The standard list is still the default and unchanged by the copy.
            (await pricing.PriceAsync(companyId, null, gadgetId, 1m)).Value.PriceListCode.Should().Be("PMSTD");

            // Validation.
            (await pricing.AdjustPricesAsync(companyId, standardId, -100m, 0m, PriceRounding.Nearest)).Error.Code.Should().Be("Price.Percent");
            (await pricing.AdjustPricesAsync(companyId, 999_999, 5m, 0m, PriceRounding.Nearest)).Error.Code.Should().Be("Price.ListNotFound");
            (await pricing.CopyPriceListAsync(companyId, standardId, "PMTRADE", "Again", 0m, 0m, PriceRounding.Nearest)).Error.Code.Should().Be("Price.DuplicateCode");
            (await pricing.AdjustPricesAsync(companyId, standardId, 0m, 0m, PriceRounding.Nearest)).Value.Should().Be(0); // nothing moves
        }
    }
}
