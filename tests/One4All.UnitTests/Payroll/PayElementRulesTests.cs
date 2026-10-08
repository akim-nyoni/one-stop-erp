using FluentAssertions;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers a pay element's floor, ceiling and rounding rules and its expense account per department.</summary>
public sealed class PayElementRulesTests
{
    [Fact]
    public void Floor_ceiling_and_rounding_shape_the_amount()
    {
        PayElement nssa = new(1, "NSSA", "Social security", PayElementType.Deduction, CalculationBasis.Formula, 1, "2400") { Ceiling = 50m };
        nssa.ApplyLimits(90m).Should().Be(50m);
        nssa.ApplyLimits(30.456m).Should().Be(30.46m);

        PayElement minimum = new(1, "UNION", "Union", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 1, "2400") { Floor = 5m };
        minimum.ApplyLimits(2m).Should().Be(5m);
        minimum.ApplyLimits(0m).Should().Be(0m, "a floor does not create an amount that is not there");

        new PayElement(1, "A", "A", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { RoundTo = 5m, Rounding = PayRounding.Up }.ApplyLimits(101m).Should().Be(105m);
        new PayElement(1, "A", "A", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { RoundTo = 5m, Rounding = PayRounding.Down }.ApplyLimits(104.99m).Should().Be(100m);
        new PayElement(1, "A", "A", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { RoundTo = 0.05m }.ApplyLimits(10.024m).Should().Be(10.00m);
    }

    [Fact]
    public void Departments_can_post_to_their_own_expense_account()
    {
        PayElement basic = new(1, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { DepartmentAccounts = "OPS=6110; fin = 6120" };
        basic.AccountFor("OPS", "6100").Should().Be("6110");
        basic.AccountFor("FIN", "6100").Should().Be("6120");
        basic.AccountFor("ADM", "6100").Should().Be("6100");
        basic.AccountFor(null, "6100").Should().Be("6100");
    }
}
