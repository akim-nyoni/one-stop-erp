using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class RatesClearanceTests
{
    [Fact]
    public void Arrears_plus_advance_months_are_due()
    {
        ClearanceQuote quote = RatesClearance.Quote(270m, 270m, 3, 2);
        quote.AverageMonthlyBilling.Should().Be(90m);
        quote.AdvanceRequired.Should().Be(180m);
        quote.AmountDue.Should().Be(450m);
        quote.CanIssue.Should().BeFalse();
    }

    [Fact]
    public void A_credit_covering_the_advance_allows_issue()
    {
        ClearanceQuote quote = RatesClearance.Quote(-180m, 270m, 3, 2);
        quote.AmountDue.Should().Be(0m);
        quote.CanIssue.Should().BeTrue();
    }

    [Fact]
    public void A_credit_short_of_the_advance_leaves_the_difference_due()
    {
        RatesClearance.Quote(-100m, 270m, 3, 2).AmountDue.Should().Be(80m);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void No_look_back_or_no_advance_months_needs_no_advance(int lookBack, int months)
    {
        RatesClearance.Quote(0m, 270m, lookBack, months).AdvanceRequired.Should().Be(0m);
    }

    [Fact]
    public void The_average_rounds_to_cents()
    {
        RatesClearance.Quote(0m, 100m, 3, 1).AverageMonthlyBilling.Should().Be(33.33m);
    }
}
