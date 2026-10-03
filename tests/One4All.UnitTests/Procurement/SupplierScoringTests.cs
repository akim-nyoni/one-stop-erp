using FluentAssertions;
using One4All.Application.Procurement;
using Xunit;

namespace One4All.UnitTests.Procurement;

/// <summary>Covers the supplier scorecard arithmetic: rates and on-time judgement.</summary>
public sealed class SupplierScoringTests
{
    [Fact]
    public void Rates_are_percentages_to_one_place_or_nothing_when_unmeasured()
    {
        SupplierScoring.Rate(1m, 2m).Should().Be(50m);
        SupplierScoring.Rate(2m, 3m).Should().Be(66.7m);
        SupplierScoring.Rate(18m, 20m).Should().Be(90m);
        SupplierScoring.Rate(0m, 0m).Should().BeNull();
    }

    [Fact]
    public void A_delivery_is_on_time_when_received_by_the_required_date()
    {
        SupplierScoring.OnTime(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10)).Should().BeTrue();
        SupplierScoring.OnTime(new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 10)).Should().BeFalse();
        SupplierScoring.OnTime(new DateOnly(2026, 3, 11), null).Should().BeNull();
    }
}
