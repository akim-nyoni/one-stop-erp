using FluentAssertions;
using One4All.Core.FixedAssets;
using Xunit;

namespace One4All.UnitTests.FixedAssets;

/// <summary>Covers when an asset is due a month's depreciation: acquisition month, already-charged months and status.</summary>
public sealed class DepreciationScheduleRuleTests
{
    private static FixedAsset Asset(DateOnly acquired) =>
        new(1, "T-1", "Test asset", "Equipment", 1_200m, 0m, acquired, DepreciationMethod.StraightLine, 12, 0m, "1600", "1650", "6000");

    [Fact]
    public void Not_due_before_the_acquisition_month_and_due_from_it()
    {
        FixedAsset asset = Asset(new DateOnly(2026, 3, 20));

        asset.SkipReasonFor(new DateOnly(2026, 2, 28)).Should().Be(DepreciationSkip.NotYetAcquired);
        asset.SkipReasonFor(new DateOnly(2026, 3, 1)).Should().BeNull(); // full-month convention: the acquisition month counts
        asset.SkipReasonFor(new DateOnly(2026, 4, 30)).Should().BeNull();
    }

    [Fact]
    public void A_charged_month_is_not_due_again_but_the_next_month_is()
    {
        FixedAsset asset = Asset(new DateOnly(2026, 1, 1));
        asset.ApplyDepreciation(asset.MonthlyDepreciation(), new DateOnly(2026, 1, 31));

        asset.LastDepreciatedPeriod.Should().Be(202601);
        asset.SkipReasonFor(new DateOnly(2026, 1, 15)).Should().Be(DepreciationSkip.AlreadyDepreciated);
        asset.SkipReasonFor(new DateOnly(2025, 12, 31)).Should().Be(DepreciationSkip.NotYetAcquired);
        asset.SkipReasonFor(new DateOnly(2026, 2, 28)).Should().BeNull();
        asset.AccumulatedDepreciation.Should().Be(100m);
    }

    [Fact]
    public void Disposed_or_fully_depreciated_assets_are_not_due()
    {
        FixedAsset disposed = Asset(new DateOnly(2026, 1, 1));
        disposed.Dispose();
        disposed.SkipReasonFor(new DateOnly(2026, 2, 1)).Should().Be(DepreciationSkip.NotActive);

        FixedAsset spent = Asset(new DateOnly(2026, 1, 1));
        spent.ApplyDepreciation(1_200m, new DateOnly(2026, 1, 31));
        spent.Status.Should().Be(FixedAssetStatus.FullyDepreciated);
        spent.SkipReasonFor(new DateOnly(2026, 2, 1)).Should().Be(DepreciationSkip.NotActive);
    }

    [Fact]
    public void Period_key_is_year_and_month()
    {
        FixedAsset.PeriodOf(new DateOnly(2026, 11, 3)).Should().Be(202611);
    }
}
