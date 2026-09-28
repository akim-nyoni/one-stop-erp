using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the cash-up arithmetic: expected drawer cash and the over/short variance.</summary>
public sealed class CashUpCalculationTests
{
    [Fact]
    public void Expected_cash_is_float_plus_cash_received_less_change()
    {
        CashUpCalculation calc = new(OpeningFloat: 100m, CashReceived: 25m, ChangeGiven: 2m, CountedCash: 123m);

        calc.ExpectedCash.Should().Be(123m); // 100 + 25 − 2
        calc.Variance.Should().Be(0m);
        calc.Balances.Should().BeTrue();
        calc.IsOver.Should().BeFalse();
        calc.IsShort.Should().BeFalse();
    }

    [Fact]
    public void A_surplus_count_is_an_overage()
    {
        CashUpCalculation calc = new(100m, 25m, 2m, CountedCash: 128m);

        calc.Variance.Should().Be(5m);
        calc.IsOver.Should().BeTrue();
        calc.IsShort.Should().BeFalse();
    }

    [Fact]
    public void A_deficit_count_is_a_shortage()
    {
        CashUpCalculation calc = new(100m, 25m, 2m, CountedCash: 120m);

        calc.Variance.Should().Be(-3m);
        calc.IsShort.Should().BeTrue();
        calc.IsOver.Should().BeFalse();
    }
}
