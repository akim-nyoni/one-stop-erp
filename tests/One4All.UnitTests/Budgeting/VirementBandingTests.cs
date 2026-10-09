using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers virement approval bands: the band an amount falls in sets the approver's level, one more between budgets.</summary>
public sealed class VirementBandingTests
{
    private static readonly VirementBandRule[] Bands = [new(10_000m, 1), new(100_000m, 3), new(null, 5)];

    [Fact]
    public void The_amount_picks_the_band_and_a_virement_between_budgets_needs_one_level_more()
    {
        VirementBanding.RequiredLevel([], 1m, false).Should().BeNull("no bands, no limit");
        VirementBanding.RequiredLevel(Bands, 10_000m, false).Should().Be(1);
        VirementBanding.RequiredLevel(Bands, 10_000.01m, false).Should().Be(3);
        VirementBanding.RequiredLevel(Bands, 5_000_000m, false).Should().Be(5);
        VirementBanding.RequiredLevel(Bands, 500m, true).Should().Be(2);
        VirementBanding.RequiredLevel(Bands, 5_000_000m, true).Should().Be(5, "the top level is the ceiling");
        VirementBanding.RequiredLevel([new(1_000m, 2)], 2_000m, false).Should().Be(5, "beyond the last band needs the top level");
    }

    [Fact]
    public void Bands_need_levels_one_to_five_distinct_limits_and_rising_levels()
    {
        VirementBanding.Problem(Bands).Should().BeNull();
        VirementBanding.Problem([new(1_000m, 6)]).Should().NotBeNull();
        VirementBanding.Problem([new(1_000m, 1), new(1_000m, 2)]).Should().NotBeNull();
        VirementBanding.Problem([new(null, 1), new(null, 2)]).Should().NotBeNull();
        VirementBanding.Problem([new(1_000m, 3), new(5_000m, 2)]).Should().NotBeNull();
    }
}
