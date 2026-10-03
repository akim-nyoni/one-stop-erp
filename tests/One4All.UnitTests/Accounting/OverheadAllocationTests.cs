using FluentAssertions;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers sharing an overhead pool by drivers and validating a rule's drivers.</summary>
public sealed class OverheadAllocationTests
{
    [Fact]
    public void Shares_follow_the_drivers_and_add_up_to_the_pool()
    {
        OverheadAllocation.Share(1_000m, [100m, 300m, 100m]).Should().Equal(200m, 600m, 200m);
        OverheadAllocation.Share(500m, [6m, 3m]).Should().Equal(333.33m, 166.67m);
        OverheadAllocation.Share(100m, [1m, 1m, 1m]).Should().Equal(33.34m, 33.33m, 33.33m);
    }

    [Fact]
    public void A_credit_pool_is_shared_as_credits()
    {
        decimal[] shares = OverheadAllocation.Share(-100m, [1m, 1m, 1m]);

        shares.Should().Equal(-33.34m, -33.33m, -33.33m);
        shares.Sum().Should().Be(-100m);
    }

    [Theory]
    [InlineData(AllocationBasis.FixedPercent, new[] { 60.0, 40.0 }, true)]
    [InlineData(AllocationBasis.FixedPercent, new[] { 60.0, 30.0 }, false)]
    [InlineData(AllocationBasis.Headcount, new[] { 12.0, 3.0 }, true)]
    [InlineData(AllocationBasis.FloorArea, new[] { 0.0, 0.0 }, false)]
    [InlineData(AllocationBasis.UsageDriver, new[] { 5.0, -1.0 }, false)]
    [InlineData(AllocationBasis.UsageDriver, new double[0], false)]
    public void Drivers_are_checked_against_the_basis(AllocationBasis basis, double[] drivers, bool valid)
    {
        (OverheadAllocation.Validate(basis, [.. drivers.Select(d => (decimal)d)]) is null).Should().Be(valid);
    }
}
