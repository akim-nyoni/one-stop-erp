using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class GrantRecognitionTests
{
    [Theory]
    [InlineData(400000.0, 100000.0)]   // a quarter of the budget spent: a quarter recognised
    [InlineData(1000000.0, 250000.0)]  // all spent: all recognised
    [InlineData(1200000.0, 250000.0)]  // overspent: capped at the grant
    [InlineData(0.0, 0.0)]
    public void Income_follows_the_expenditure_it_funds(double spent, double recognised)
    {
        GrantRecognition.TargetToDate(250000m, 1000000m, (decimal)spent, conditionsAssured: true).Should().Be((decimal)recognised);
    }

    [Fact]
    public void Nothing_is_recognised_until_the_conditions_are_assured()
    {
        GrantRecognition.TargetToDate(250000m, 1000000m, 400000m, conditionsAssured: false).Should().Be(0m);
    }
}
