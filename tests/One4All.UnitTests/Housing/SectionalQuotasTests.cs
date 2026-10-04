using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class SectionalQuotasTests
{
    [Fact]
    public void Quotas_follow_floor_area()
    {
        SectionalQuotas.FromFloorAreas([new("1A", 50m), new("1B", 75m), new("2A", 75m)])
            .Should().Equal(new UnitAmount("1A", 25m), new UnitAmount("1B", 37.5m), new UnitAmount("2A", 37.5m));
    }

    [Fact]
    public void Quota_rounding_lands_on_the_largest_unit_and_totals_100()
    {
        IReadOnlyList<UnitAmount> quotas = SectionalQuotas.FromFloorAreas([new("1", 60m), new("2", 60m), new("3", 61m)]);
        quotas.Sum(q => q.Amount).Should().Be(100m);
        quotas.Select(q => q.Amount).Should().Equal(33.1492m, 33.1492m, 33.7016m);
    }

    [Fact]
    public void Units_need_floor_area()
    {
        FluentActions.Invoking(() => SectionalQuotas.FromFloorAreas([new("1", 0m)])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_levy_splits_by_quota_to_the_cent()
    {
        UnitAmount[] quotas = [new("1A", 33.3333m), new("1B", 33.3333m), new("2A", 33.3334m)];
        IReadOnlyList<UnitAmount> split = SectionalQuotas.SplitLevy(quotas, 100.01m);
        split.Sum(s => s.Amount).Should().Be(100.01m);
        split.Select(s => s.Amount).Should().Equal(33.34m, 33.34m, 33.33m);
    }
}
