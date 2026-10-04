using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class LeaseScheduleTests
{
    private static readonly IReadOnlyList<LeaseYear> ThreeYears = LeaseSchedule.Years(12000m, 3, 1, 10m);

    [Fact]
    public void Rent_escalates_at_each_review()
    {
        ThreeYears.Select(y => y.AnnualRent).Should().Equal(12000m, 13200m, 14520m);
        LeaseSchedule.Years(12000m, 6, 3, 10m).Select(y => y.AnnualRent).Should().Equal(12000m, 12000m, 12000m, 13200m, 13200m, 13200m);
    }

    [Fact]
    public void Monthly_rent_is_the_year_over_twelve()
    {
        LeaseSchedule.ContractualMonth(ThreeYears, 1).Should().Be(1000m);
        LeaseSchedule.ContractualMonth(ThreeYears, 13).Should().Be(1100m);
        LeaseSchedule.ContractualMonth(ThreeYears, 25).Should().Be(1210m);
        Enumerable.Range(1, 36).Sum(m => LeaseSchedule.ContractualMonth(ThreeYears, m)).Should().Be(39720m);
    }

    [Fact]
    public void Straight_line_income_spreads_the_total_and_clears_exactly()
    {
        LeaseSchedule.StraightLineMonth(ThreeYears, 1).Should().Be(1103.33m);
        LeaseSchedule.StraightLineMonth(ThreeYears, 36).Should().Be(1103.45m); // takes the 0.12 rounding
        Enumerable.Range(1, 36).Sum(m => LeaseSchedule.StraightLineMonth(ThreeYears, m)).Should().Be(39720m);
    }

    [Fact]
    public void A_year_that_does_not_divide_evenly_takes_rounding_in_its_last_month()
    {
        IReadOnlyList<LeaseYear> odd = LeaseSchedule.Years(1000m, 1, 1, 0m);
        LeaseSchedule.ContractualMonth(odd, 1).Should().Be(83.33m);
        LeaseSchedule.ContractualMonth(odd, 12).Should().Be(83.37m);
    }
}
