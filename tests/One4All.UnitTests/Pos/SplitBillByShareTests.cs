using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers splitting a bill by percentages and by the amounts each payer will pay.</summary>
public sealed class SplitBillByShareTests
{
    private static readonly PosSaleLineRequest[] Basket = [new(1, "Pizza", 3m, 10m, 30m, 4.5m, "4000"), new(2, "Wine", 1m, 20m, 20m, 3m, "4000")];

    [Fact]
    public void A_bill_splits_sixty_forty_by_percentage_or_by_amount()
    {
        IReadOnlyList<IReadOnlyList<SplitShare>> shares = SplitBill.ByPercent(Basket, [60m, 40m]).Value;
        shares[0].Select(s => s.Quantity).Should().Equal(1.8m, 0.6m);
        shares[1].Select(s => s.Quantity).Should().Equal(1.2m, 0.4m);
        var plan = SplitBill.Plan(Basket, shares).Value;
        plan.Select(SplitBill.Gross).Should().Equal(34.5m, 23m);

        SplitBill.ByAmount(Basket, [34.5m, 23m]).Value.Should().BeEquivalentTo(shares, "the same split, given as amounts");
        SplitBill.ByPercent(Basket, [60m, 30m]).Error.Code.Should().Be("Pos.SplitPercent");
        SplitBill.ByAmount(Basket, [30m, 20m]).Error.Code.Should().Be("Pos.SplitAmount");
    }

    [Fact]
    public void Odd_amounts_still_share_out_every_line_in_full()
    {
        IReadOnlyList<IReadOnlyList<SplitShare>> shares = SplitBill.ByAmount(Basket, [10m, 20m, 27.5m]).Value;
        for (int line = 0; line < Basket.Length; line++)
        {
            shares.SelectMany(p => p).Where(s => s.LineIndex == line).Sum(s => s.Quantity).Should().Be(Basket[line].Quantity);
        }

        SplitBill.Plan(Basket, shares).Value.Sum(SplitBill.Gross).Should().Be(57.5m);
    }
}
