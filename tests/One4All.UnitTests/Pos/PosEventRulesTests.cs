using FluentAssertions;
using One4All.Application.Pos;
using One4All.Core.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

public sealed class PosEventRulesTests
{
    [Theory]
    [InlineData(PosEventKind.LineVoid, null, null, null, "Pos.ReasonRequired")]
    [InlineData(PosEventKind.NoSale, "float", null, null, "Pos.AuthorisationRequired")]
    [InlineData(PosEventKind.NoSale, "float", 1L, null, "Pos.SelfAuthorised")]
    [InlineData(PosEventKind.PriceOverride, "match", 2L, null, "Pos.OriginalPriceRequired")]
    [InlineData(PosEventKind.LoyaltyEarned, null, null, null, "Pos.LoyaltyCustomer")]
    [InlineData(PosEventKind.Reprint, null, null, null, null)]
    [InlineData(PosEventKind.NoSale, "float", 2L, null, null)]
    [InlineData(PosEventKind.LoyaltyRedeemed, null, null, "CARD-1", null)]
    public void Each_kind_needs_what_it_needs(PosEventKind kind, string? reason, long? authoriser, string? customer, string? expected)
        => PosEventRules.Validate(kind, 1m, null, reason, 1, authoriser, customer)?.Code.Should().Be(expected);

    [Fact]
    public void A_discount_cannot_exceed_the_list_amount()
    {
        PosEventRules.Validate(PosEventKind.LineDiscount, 5m, 4m, "dented", 1, null, null)!.Code.Should().Be("Pos.DiscountTooLarge");
        PosEventRules.Validate(PosEventKind.LineDiscount, 4m, 4m, "dented", 1, null, null).Should().BeNull();
        PosEventRules.Validate(PosEventKind.Reprint, -1m, null, null, 1, null, null)!.Code.Should().Be("Pos.EventAmount");
    }

    [Fact]
    public void Loyalty_balance_is_earned_less_redeemed()
        => PosEventRules.Loyalty([(PosEventKind.LoyaltyEarned, 10m), (PosEventKind.LoyaltyRedeemed, 4m), (PosEventKind.LoyaltyEarned, 2.5m), (PosEventKind.Reprint, 99m)])
            .Should().Be((12.5m, 4m, 8.5m));
}
