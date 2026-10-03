using FluentAssertions;
using One4All.Application.Vouchers;
using One4All.Core.Vouchers;
using Xunit;

namespace One4All.UnitTests.Vouchers;

/// <summary>Covers promotion discounts: percentages to the cent, fixed amounts, never more than the spend.</summary>
public sealed class PromoPricingTests
{
    [Theory]
    [InlineData(PromoDiscountType.Percent, 10, 85.55, 8.56)]
    [InlineData(PromoDiscountType.Amount, 20, 85.55, 20)]
    [InlineData(PromoDiscountType.Amount, 20, 15, 15)]          // capped at the spend
    [InlineData(PromoDiscountType.Percent, 150, 40, 40)]        // a percentage over 100 counts as 100
    [InlineData(PromoDiscountType.Percent, 10, 0, 0)]
    public void The_discount_follows_the_type_and_never_exceeds_the_spend(PromoDiscountType type, double value, double spend, double discount)
    {
        PromoPricing.Discount(type, (decimal)value, (decimal)spend).Should().Be((decimal)discount);
    }
}
