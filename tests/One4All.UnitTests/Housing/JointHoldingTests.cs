using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class JointHoldingTests
{
    private static readonly HolderShare[] Three = [new(1, 50m), new(2, 30m), new(3, 20m)];

    [Fact]
    public void Shares_must_be_positive_unique_and_total_100()
    {
        JointHolding.Problem(Three).Should().BeNull();
        JointHolding.Problem([new(1, 60m), new(2, 30m)]).Should().Contain("90%");
        JointHolding.Problem([new(1, 50m), new(1, 50m)]).Should().Contain("more than once");
        JointHolding.Problem([new(1, 100m), new(2, 0m)]).Should().Contain("positive");
        JointHolding.Problem([]).Should().Contain("at least one");
    }

    [Fact]
    public void Part_of_a_share_can_be_transferred_to_a_newcomer()
    {
        JointHolding.TransferShare(Three, 1, 9, 20m).Should().Equal(new HolderShare(1, 30m), new HolderShare(2, 30m), new HolderShare(3, 20m), new HolderShare(9, 20m));
    }

    [Fact]
    public void A_whole_share_transferred_to_another_holder_merges_and_drops_the_transferor()
    {
        JointHolding.TransferShare(Three, 3, 2, 20m).Should().Equal(new HolderShare(1, 50m), new HolderShare(2, 50m));
    }

    [Fact]
    public void More_than_is_held_or_to_oneself_cannot_be_transferred()
    {
        JointHolding.TransferShare(Three, 3, 2, 25m).Should().BeNull();
        JointHolding.TransferShare(Three, 4, 2, 5m).Should().BeNull();
        JointHolding.TransferShare(Three, 2, 2, 5m).Should().BeNull();
    }

    [Fact]
    public void Under_joint_tenancy_the_survivors_take_the_share_in_proportion()
    {
        JointHolding.OnDeath(Three, 1, CoOwnershipRegime.JointWithSurvivorship).Should().Equal(new HolderShare(2, 60m), new HolderShare(3, 40m));
    }

    [Fact]
    public void Survivorship_rounding_goes_to_the_largest_survivor_and_still_totals_100()
    {
        HolderShare[] thirds = [new(1, 33.3333m), new(2, 33.3333m), new(3, 33.3334m)];
        IReadOnlyList<HolderShare> after = JointHolding.OnDeath(thirds, 2, CoOwnershipRegime.JointWithSurvivorship)!;
        after.Sum(h => h.SharePercent).Should().Be(100m);
        after.Select(h => h.CustomerId).Should().Equal(1, 3);
    }

    [Fact]
    public void Under_tenancy_in_common_the_share_stays_with_the_estate()
    {
        JointHolding.OnDeath(Three, 1, CoOwnershipRegime.InCommon).Should().Equal(Three);
        JointHolding.OnDeath(Three, 7, CoOwnershipRegime.InCommon).Should().BeNull();
    }
}
