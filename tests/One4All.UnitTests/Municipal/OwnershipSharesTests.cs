using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class OwnershipSharesTests
{
    [Fact]
    public void Joint_owners_with_full_shares_and_one_ratepayer_are_in_order()
    {
        OwnershipShares.Problem([new OwnerShare(1, 60m, true), new OwnerShare(2, 40m, false)]).Should().BeNull();
    }

    [Fact]
    public void Shares_must_total_one_hundred()
    {
        OwnershipShares.Problem([new OwnerShare(1, 60m, true), new OwnerShare(2, 30m, false)]).Should().Contain("90%");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Exactly_one_owner_is_the_ratepayer(bool first, bool second)
    {
        OwnershipShares.Problem([new OwnerShare(1, 50m, first), new OwnerShare(2, 50m, second)]).Should().Contain("ratepayer");
    }

    [Fact]
    public void An_owner_cannot_be_listed_twice()
    {
        OwnershipShares.Problem([new OwnerShare(1, 50m, true), new OwnerShare(1, 50m, false)]).Should().Contain("more than once");
    }

    [Fact]
    public void No_owners_or_a_zero_share_is_refused()
    {
        OwnershipShares.Problem([]).Should().NotBeNull();
        OwnershipShares.Problem([new OwnerShare(1, 100m, true), new OwnerShare(2, 0m, false)]).Should().Contain("positive");
    }
}
