using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers promotion eligibility: a 10%-off campaign for STAFF customers on BAKERY items with a minimum spend of 20 refuses other customer
/// types, needs the basket, counts only the bakery lines for the minimum and the discount, and refuses a basket with no bakery lines.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PromoEligibilityTests
{
    private readonly AccountingFixture _fixture;

    public PromoEligibilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Campaigns_limited_to_customer_types_and_categories_discount_only_what_qualifies()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        IPromoService promos = scope.ServiceProvider.GetRequiredService<IPromoService>();
        (await promos.CreateCampaignAsync(companyId, new PromoCampaignRequest("STAFFBAKE", "Staff bakery", PromoDiscountType.Percent, 10m, new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31), 20m, 5, null, ["SB-1"], "Staff, Pensioner", "BAKERY"))).IsSuccess.Should().BeTrue();

        DateOnly date = new(2026, 3, 10);
        PromoBasketLine[] basket = [new("BAKERY", 30m), new("DAIRY", 70m)];
        (await promos.QuoteAsync(companyId, "SB-1", date, 100m, new PromoEligibility("Retail", basket))).Error.Code.Should().Be("Promo.CustomerNotEligible");
        (await promos.QuoteAsync(companyId, "SB-1", date, 100m)).Error.Code.Should().Be("Promo.CustomerNotEligible");
        (await promos.QuoteAsync(companyId, "SB-1", date, 100m, new PromoEligibility("staff", []))).Error.Code.Should().Be("Promo.BasketNeeded");
        (await promos.QuoteAsync(companyId, "SB-1", date, 100m, new PromoEligibility("STAFF", basket))).Value.Discount.Should().Be(3m, "10% of the 30 of bakery only");
        (await promos.QuoteAsync(companyId, "SB-1", date, 85m, new PromoEligibility("Staff", [new("BAKERY", 15m), new("DAIRY", 70m)]))).Error.Code.Should().Be("Promo.MinimumSpend");
        (await promos.QuoteAsync(companyId, "SB-1", date, 70m, new PromoEligibility("Staff", [new("DAIRY", 70m)]))).Error.Code.Should().Be("Promo.NoEligibleItems");
        (await promos.RedeemAsync(companyId, "SB-1", date, 100m, "SALE-1", new PromoEligibility("Pensioner", basket))).Value.Discount.Should().Be(3m);
    }
}
