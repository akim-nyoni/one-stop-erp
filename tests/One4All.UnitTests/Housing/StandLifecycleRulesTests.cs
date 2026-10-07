using FluentAssertions;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

/// <summary>Covers the stand state machine: the main line, no jumps, holds and the consolidation / re-subdivision branches.</summary>
public sealed class StandLifecycleRulesTests
{
    [Theory]
    [InlineData(StandStatus.RawLand, StandStatus.Planned, true)]
    [InlineData(StandStatus.RawLand, StandStatus.Surveyed, false)]
    [InlineData(StandStatus.Serviced, StandStatus.Available, true)]
    [InlineData(StandStatus.Available, StandStatus.Sold, false)]
    [InlineData(StandStatus.Allocated, StandStatus.Sold, true)]
    [InlineData(StandStatus.Sold, StandStatus.TitleApplied, false)]
    [InlineData(StandStatus.FullyPaid, StandStatus.TitleApplied, true)]
    [InlineData(StandStatus.TitleRegistered, StandStatus.TitleHandedOver, true)]
    [InlineData(StandStatus.Offered, StandStatus.OfferCancelled, true)]
    [InlineData(StandStatus.Sold, StandStatus.Withheld, true)]
    [InlineData(StandStatus.Withheld, StandStatus.Withheld, false)]
    [InlineData(StandStatus.TitleHandedOver, StandStatus.Withheld, false)]
    [InlineData(StandStatus.Surveyed, StandStatus.ConsolidatedAway, true)]
    [InlineData(StandStatus.Allocated, StandStatus.ReSubdivided, false)]
    public void Moves_follow_the_lifecycle(StandStatus from, StandStatus to, bool allowed) => StandLifecycle.CanMove(from, to).Should().Be(allowed);

    [Fact]
    public void A_stand_moved_by_hand_is_audited_and_a_hold_returns_where_it_was()
    {
        HousingStand stand = new(1, 1, null, "u-1", 300m, 1000m, new DateOnly(2026, 1, 1), StandStatus.Surveyed);
        stand.MoveTo(StandStatus.Serviced, new DateOnly(2026, 2, 1), 5, "Res", null).IsFailure.Should().BeTrue();
        stand.MoveTo(StandStatus.UnderServicing, new DateOnly(2026, 2, 1), 5, "Contract C1", null).IsSuccess.Should().BeTrue();
        stand.MoveTo(StandStatus.Withheld, new DateOnly(2026, 2, 2), 5, "Caveat", null).IsSuccess.Should().BeTrue();
        stand.MoveTo(StandStatus.Serviced, new DateOnly(2026, 2, 3), 5, "Res", null).Error.Code.Should().Be("Hse.StandWithheld");
        stand.MoveTo(StandStatus.UnderServicing, new DateOnly(2026, 2, 4), 5, "Lifted", null).IsSuccess.Should().BeTrue();
        stand.StatusChanges.Select(c => c.To).Should().Equal(StandStatus.UnderServicing, StandStatus.Withheld, StandStatus.UnderServicing);
        stand.StatusBeforeHold.Should().BeNull();
    }
}
