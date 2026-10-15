using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers cash pay by notes and coins, per employee and per pay point.</summary>
public sealed class CashPayBreakdownTests
{
    private static readonly decimal[] Usd = [1m, 5m, 10m, 20m, 50m, 100m, 0.25m, 0.10m];

    [Fact]
    public void Net_pay_is_made_up_of_the_fewest_notes_largest_first()
    {
        CashPieces pieces = CashPayBreakdown.Split(387.40m, Usd);
        pieces.Describe().Should().Be("3 × 100, 1 × 50, 1 × 20, 1 × 10, 1 × 5, 2 × 1, 1 × 0.25, 1 × 0.1, remainder 0.05");
        pieces.Remainder.Should().Be(0.05m);
    }

    [Fact]
    public void A_pay_point_draws_the_sum_of_its_envelopes()
    {
        CashPieces total = CashPayBreakdown.Total([CashPayBreakdown.Split(150m, Usd), CashPayBreakdown.Split(75m, Usd), CashPayBreakdown.Split(0.30m, Usd)]);
        total.Pieces.Should().BeEquivalentTo(new Dictionary<decimal, int> { [100m] = 1, [50m] = 2, [20m] = 1, [5m] = 1, [0.25m] = 1 });
        total.Remainder.Should().Be(0.05m);
        total.Describe().Should().Be("1 × 100, 2 × 50, 1 × 20, 1 × 5, 1 × 0.25, remainder 0.05");
    }
}
