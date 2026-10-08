using FluentAssertions;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers pay periods per frequency: the window a pay date pays for, periods a year, the tax-year period number, and part-period days.</summary>
public sealed class PayPeriodsTests
{
    [Fact]
    public void Windows_and_period_numbers_follow_the_frequency()
    {
        PayPeriods.Window(PayFrequency.Monthly, new DateOnly(2026, 2, 25)).Should().Be((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        PayPeriods.Window(PayFrequency.Weekly, new DateOnly(2026, 1, 9)).Should().Be((new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 9)));
        PayPeriods.Window(PayFrequency.Fortnightly, new DateOnly(2026, 1, 16)).Should().Be((new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 16)));
        (PayPeriods.PerYear(PayFrequency.Monthly), PayPeriods.PerYear(PayFrequency.Fortnightly), PayPeriods.PerYear(PayFrequency.Weekly)).Should().Be((12, 26, 52));
        PayPeriods.Number(PayFrequency.Weekly, new DateOnly(2026, 1, 9)).Should().Be(2);
        PayPeriods.Number(PayFrequency.Monthly, new DateOnly(2026, 7, 25)).Should().Be(7);
        PayPeriods.Number(PayFrequency.Weekly, new DateOnly(2026, 12, 31)).Should().Be(52, "the 53rd part-week counts with the last week");
    }

    [Fact]
    public void Joiners_and_leavers_are_paid_for_the_days_employed()
    {
        PayPeriods.Days(PayFrequency.Monthly, new DateOnly(2026, 1, 25), new DateOnly(2026, 1, 11), null).Should().Be((21, 31));
        PayPeriods.Days(PayFrequency.Monthly, new DateOnly(2026, 1, 25), new DateOnly(2020, 1, 1), new DateOnly(2026, 1, 10)).Should().Be((10, 31));
        PayPeriods.Days(PayFrequency.Monthly, new DateOnly(2026, 1, 25), new DateOnly(2020, 1, 1), null).Should().Be((31, 31));
        PayPeriods.Days(PayFrequency.Weekly, new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 7), null).Should().Be((3, 7));
        PayPeriods.Days(PayFrequency.Monthly, new DateOnly(2026, 1, 25), new DateOnly(2026, 2, 3), null).Should().Be((0, 31), "not yet started");
    }
}
