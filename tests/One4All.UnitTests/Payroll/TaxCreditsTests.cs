using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers tax credits: fixed and element-based credits, age limits, and never taking tax below nil.</summary>
public sealed class TaxCreditsTests
{
    [Fact]
    public void Credits_come_off_the_tax_by_age_and_by_element_and_never_below_nil()
    {
        DateOnly payDate = new(2026, 6, 25);
        TaxCredits.Eligible(null, null, payDate).Should().BeTrue();
        TaxCredits.Eligible(65, new DateOnly(1961, 6, 25), payDate).Should().BeTrue("65 on the pay date");
        TaxCredits.Eligible(65, new DateOnly(1961, 6, 26), payDate).Should().BeFalse("65 tomorrow");
        TaxCredits.Eligible(65, null, payDate).Should().BeFalse("no date of birth, no age credit");

        TaxCreditRule[] rules = [new("ELDER", TaxCreditBasis.Fixed, 75m, 0m, null), new("MED", TaxCreditBasis.PercentOfElement, 0m, 50m, "MEDAID")];
        TaxCredits.Total(rules, code => code == "MEDAID" ? 40m : 0m, 1m).Should().Be(95m, "75 plus half of 40");
        TaxCredits.Total(rules, _ => 0m, 12m / 52m).Should().Be(17.31m, "a week's share of the monthly credit");
        TaxCredits.Apply(300m, 95m).Should().Be(205m);
        TaxCredits.Apply(50m, 95m).Should().Be(0m, "credits are not refunded");
    }
}
