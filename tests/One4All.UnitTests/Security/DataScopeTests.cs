using FluentAssertions;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers row-level scopes: restricted only when every assignment restricts a dimension, matched by code or id.</summary>
public sealed class DataScopeTests
{
    private static IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>> Assignment(params (ScopeDimension Dimension, string[] Values)[] scopes)
        => scopes.ToDictionary(s => s.Dimension, s => (IReadOnlyList<string>)s.Values);

    [Fact]
    public void A_dimension_is_restricted_to_the_listed_values()
    {
        DataScope scope = DataScope.From([Assignment((ScopeDimension.Warehouse, ["SHOP", "12"]))], false);
        scope.Allows(ScopeDimension.Warehouse, "shop").Should().BeTrue();
        scope.Allows(ScopeDimension.Warehouse, "BACK", "12").Should().BeTrue();
        scope.Allows(ScopeDimension.Warehouse, "BACK", "13").Should().BeFalse();
        scope.Allows(ScopeDimension.Branch, "ANY").Should().BeTrue();
    }

    [Fact]
    public void An_open_assignment_or_administrator_reaches_everything()
    {
        DataScope.From([Assignment((ScopeDimension.Warehouse, ["SHOP"])), Assignment()], false).Restricts(ScopeDimension.Warehouse).Should().BeFalse();
        DataScope.From([Assignment((ScopeDimension.Warehouse, ["SHOP"]))], true).Allows(ScopeDimension.Warehouse, "BACK").Should().BeTrue();
        DataScope.From([Assignment((ScopeDimension.Warehouse, ["SHOP"])), Assignment((ScopeDimension.Warehouse, ["BACK"]))], false).Allows(ScopeDimension.Warehouse, "BACK").Should().BeTrue();
    }
}
