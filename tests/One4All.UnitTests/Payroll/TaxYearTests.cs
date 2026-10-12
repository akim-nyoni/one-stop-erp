using FluentAssertions;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers a tax year that does not start in January.</summary>
public sealed class TaxYearTests
{
    [Fact]
    public void A_march_tax_year_numbers_its_periods_from_march()
    {
        PayPeriods.TaxYearStart(new DateOnly(2026, 2, 25), 3).Should().Be(new DateOnly(2025, 3, 1));
        PayPeriods.TaxYearStart(new DateOnly(2026, 3, 25), 3).Should().Be(new DateOnly(2026, 3, 1));
        PayPeriods.TaxYear(new DateOnly(2027, 1, 31), 3).Should().Be(2026);
        PayPeriods.Number(PayFrequency.Monthly, new DateOnly(2026, 3, 25), 3).Should().Be(1);
        PayPeriods.Number(PayFrequency.Monthly, new DateOnly(2027, 2, 25), 3).Should().Be(12);
        PayPeriods.Number(PayFrequency.Weekly, new DateOnly(2026, 3, 14), 3).Should().Be(2);
        PayPeriods.Number(PayFrequency.Monthly, new DateOnly(2026, 7, 25), 1).Should().Be(PayPeriods.Number(PayFrequency.Monthly, new DateOnly(2026, 7, 25)), "January is the default");
        PayPeriods.TaxYearStart(new DateOnly(2026, 7, 1), 0).Should().Be(new DateOnly(2026, 1, 1), "an unset month means January");
    }
}
