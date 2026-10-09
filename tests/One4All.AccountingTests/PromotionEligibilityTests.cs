using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers promotions for chosen customer types: a trade discount only for trade customers, nothing for an untyped or retail customer.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PromotionEligibilityTests
{
    private readonly AccountingFixture _fixture;

    public PromotionEligibilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_promotion_for_customer_types_applies_only_to_them()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4952", "Trade Discounts", AccountType.Expense));
        Item cement = new(companyId, "PE-CEM", "Cement") { RevenueAccountCode = "4000", Category = "Building" };
        db.Items.Add(cement);
        await db.SaveChangesAsync();

        IPromotionService promotions = scope.ServiceProvider.GetRequiredService<IPromotionService>();
        (await promotions.SaveAsync(new PromotionRequest
        {
            CompanyId = companyId, Code = "TRADE10", Name = "Trade 10%", Kind = PromotionKind.PercentOff, DiscountAccountCode = "4952", StartsOn = new DateOnly(2026, 1, 1), Percent = 10m,
            Category = "Building", CustomerTypes = ["Builder", " contractor "],
        })).IsSuccess.Should().BeTrue();
        decimal Discount(string? type) => promotions.PriceBasketAsync(new BasketRequest(companyId, null, new DateTime(2026, 6, 1, 10, 0, 0), false, [], [new BasketItem(cement.Id, 10m, 12m)], type))
            .GetAwaiter().GetResult().Lines.Sum(l => l.Discount);
        Discount("Contractor").Should().Be(12m, "10% of 120 for a contractor");
        Discount("builder").Should().Be(12m);
        Discount("Retail").Should().Be(0m);
        Discount(null).Should().Be(0m, "a walk-in customer has no type");
    }
}
