using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers department isolation in budget preparation.</summary>
public sealed class BudgetDepartmentAccessTests
{
    [Fact]
    public void A_department_reaches_only_its_own_budgets()
    {
        IReadOnlySet<string>? roads = BudgetDepartmentAccess.Confined(false, ["ROADS", null, " "]);
        BudgetDepartmentAccess.Allows(roads, "roads").Should().BeTrue();
        BudgetDepartmentAccess.Allows(roads, "WATER").Should().BeFalse();
        BudgetDepartmentAccess.Allows(roads, null).Should().BeFalse("the company-wide budget is Finance's");
        BudgetDepartmentAccess.Confined(true, ["ROADS"]).Should().BeNull("granted every department");
        BudgetDepartmentAccess.Confined(false, []).Should().BeNull("tied to no department");
        BudgetDepartmentAccess.Allows(null, "WATER").Should().BeTrue();
    }
}
