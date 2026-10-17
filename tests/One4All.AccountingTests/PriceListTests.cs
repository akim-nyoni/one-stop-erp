using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers price lists: quantity breaks resolve to the highest applicable break, a customer's own list wins
/// over the default and the default fills gaps, currency is enforced on assignment, and only one list is
/// the default.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PriceListTests
{
    private readonly AccountingFixture _fixture;

    public PriceListTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prices_resolve_by_quantity_break_from_customer_list_then_default()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long tradeCustomerId;
        long retailCustomerId;
        long widgetId;
        long gadgetId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer trade = new(companyId, "PL-TRADE", "Trade Buyer", Scenario.Usd);
            Customer retail = new(companyId, "PL-RETAIL", "Retail Buyer", Scenario.Usd);
            db.Customers.AddRange(trade, retail);
            Item widget = new(companyId, "PL-WIDGET", "Widget") { RevenueAccountCode = "4000" };
            Item gadget = new(companyId, "PL-GADGET", "Gadget") { RevenueAccountCode = "4000" };
            db.Items.AddRange(widget, gadget);
            await db.SaveChangesAsync();
            tradeCustomerId = trade.Id;
            retailCustomerId = retail.Id;
            widgetId = widget.Id;
            gadgetId = gadget.Id;
        }

        long standardId;
        long tradeListId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

            var standard = await pricing.CreatePriceListAsync(companyId, "std", "Standard", Scenario.Usd, isDefault: true);
            standard.IsSuccess.Should().BeTrue(standard.IsFailure ? standard.Error.ToString() : null);
            standardId = standard.Value;
            (await pricing.CreatePriceListAsync(companyId, "STD", "Duplicate", Scenario.Usd, false)).Error.Code.Should().Be("Price.DuplicateCode");

            var trade = await pricing.CreatePriceListAsync(companyId, "TRADE", "Trade", Scenario.Usd, isDefault: false);
            trade.IsSuccess.Should().BeTrue();
            tradeListId = trade.Value;

            // Standard: widget 10.00 base, 9.00 from 10, 8.00 from 100; gadget 25.00.
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 1m, 10.00m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 10m, 9.00m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 100m, 8.00m)).IsSuccess.Should().BeTrue();
            (await pricing.SetPriceAsync(companyId, standardId, gadgetId, 1m, 25.00m)).IsSuccess.Should().BeTrue();

            // Trade: widget only, 7.50 flat - gadget falls through to the default list.
            (await pricing.SetPriceAsync(companyId, tradeListId, widgetId, 1m, 7.50m)).IsSuccess.Should().BeTrue();

            // Validation.
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 0m, 1m)).Error.Code.Should().Be("Price.MinimumQuantity");
            (await pricing.SetPriceAsync(companyId, standardId, widgetId, 1m, -1m)).Error.Code.Should().Be("Price.Negative");
            (await pricing.SetPriceAsync(companyId, standardId, 999_999, 1m, 1m)).Error.Code.Should().Be("Inv.ItemNotFound");

            // Re-setting an existing break replaces it rather than adding a second line.
            (await pricing.SetPriceAsync(companyId, standardId, gadgetId, 1m, 24.00m)).IsSuccess.Should().BeTrue();

            var link = await pricing.AssignCustomerAsync(companyId, tradeCustomerId, tradeListId);
            link.IsSuccess.Should().BeTrue(link.IsFailure ? link.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

            // Retail (no list of its own) → default list, quantity breaks.
            (await pricing.PriceAsync(companyId, retailCustomerId, widgetId, 1m)).Value.Should().BeEquivalentTo(new { UnitPrice = 10.00m, MinimumQuantity = 1m, FromCustomerList = false, PriceListCode = "STD" });
            (await pricing.PriceAsync(companyId, retailCustomerId, widgetId, 9m)).Value.UnitPrice.Should().Be(10.00m);
            (await pricing.PriceAsync(companyId, retailCustomerId, widgetId, 10m)).Value.UnitPrice.Should().Be(9.00m);
            (await pricing.PriceAsync(companyId, retailCustomerId, widgetId, 250m)).Value.Should().BeEquivalentTo(new { UnitPrice = 8.00m, MinimumQuantity = 100m });
            (await pricing.PriceAsync(companyId, retailCustomerId, gadgetId, 3m)).Value.UnitPrice.Should().Be(24.00m); // replaced break

            // Trade → own list for the widget at any quantity; gadget falls through to the default.
            (await pricing.PriceAsync(companyId, tradeCustomerId, widgetId, 500m)).Value.Should().BeEquivalentTo(new { UnitPrice = 7.50m, FromCustomerList = true, PriceListCode = "TRADE" });
            (await pricing.PriceAsync(companyId, tradeCustomerId, gadgetId, 1m)).Value.Should().BeEquivalentTo(new { UnitPrice = 24.00m, FromCustomerList = false });

            // No customer → default list only; unknown item / non-positive quantity fail.
            (await pricing.PriceAsync(companyId, null, widgetId, 10m)).Value.UnitPrice.Should().Be(9.00m);
            (await pricing.PriceAsync(companyId, null, 999_999, 1m)).Error.Code.Should().Be("Price.NoPrice");
            (await pricing.PriceAsync(companyId, null, widgetId, 0m)).Error.Code.Should().Be("Price.Quantity");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // A list in another currency cannot be assigned to a USD customer.
            long eurListId = (await pricing.CreatePriceListAsync(companyId, "EUR", "Euro list", CurrencyCode.Parse("EUR"), false)).Value;
            (await pricing.AssignCustomerAsync(companyId, retailCustomerId, eurListId)).Error.Code.Should().Be("Price.CurrencyMismatch");
            (await pricing.AssignCustomerAsync(companyId, retailCustomerId, 999_999)).Error.Code.Should().Be("Price.ListNotFound");

            // Making another list the default clears the old default.
            long newDefaultId = (await pricing.CreatePriceListAsync(companyId, "STD2", "Standard v2", Scenario.Usd, isDefault: true)).Value;
            db.PriceLists.Where(p => p.CompanyId == companyId && p.IsDefault).Select(p => p.Id).Should().Equal(newDefaultId);

            // The old default no longer answers for the retail customer; the new (empty) default has no price.
            (await pricing.PriceAsync(companyId, retailCustomerId, widgetId, 1m)).Error.Code.Should().Be("Price.NoPrice");

            // Unlinking the trade customer sends them to the default too.
            (await pricing.AssignCustomerAsync(companyId, tradeCustomerId, null)).IsSuccess.Should().BeTrue();
            (await pricing.PriceAsync(companyId, tradeCustomerId, widgetId, 1m)).Error.Code.Should().Be("Price.NoPrice");
        }
    }
}
