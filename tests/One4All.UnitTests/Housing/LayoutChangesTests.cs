using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class LayoutChangesTests
{
    [Fact]
    public void Only_uses_that_changed_are_listed_in_use_order()
    {
        Dictionary<StandUse, int> before = new() { [StandUse.Residential] = 100, [StandUse.Roads] = 12, [StandUse.Schools] = 1 };
        Dictionary<StandUse, int> after = new() { [StandUse.Residential] = 96, [StandUse.Roads] = 12, [StandUse.Commercial] = 4 };
        LayoutChanges.Between(before, after).Should().Equal(
            new LayoutChange(StandUse.Residential, 100, 96), new LayoutChange(StandUse.Commercial, 0, 4), new LayoutChange(StandUse.Schools, 1, 0));
    }

    [Fact]
    public void Identical_versions_have_no_changes()
    {
        Dictionary<StandUse, int> uses = new() { [StandUse.Residential] = 10 };
        LayoutChanges.Between(uses, new Dictionary<StandUse, int>(uses)).Should().BeEmpty();
    }

    [Fact]
    public void Roads_are_not_stands()
    {
        LayoutChanges.Stands(new Dictionary<StandUse, int> { [StandUse.Residential] = 10, [StandUse.OpenSpace] = 2, [StandUse.Roads] = 5, [StandUse.Clinics] = 1 }).Should().Be(13);
    }
}
