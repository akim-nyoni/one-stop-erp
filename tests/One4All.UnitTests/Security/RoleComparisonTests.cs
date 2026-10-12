using FluentAssertions;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers comparing two roles' grants, matching permission patterns and applying bulk changes.</summary>
public sealed class RoleComparisonTests
{
    [Fact]
    public void Compare_lists_only_the_differences()
    {
        Dictionary<string, OperationFlags> clerk = new() { ["Payroll.Employee"] = OperationFlags.View | OperationFlags.Edit, ["Ar.Invoice"] = OperationFlags.View };
        Dictionary<string, OperationFlags> manager = new() { ["payroll.employee"] = OperationFlags.View | OperationFlags.Approve, ["Ar.Invoice"] = OperationFlags.View, ["Gl.Journal"] = OperationFlags.Post };
        IReadOnlyList<RoleDifference> differences = RoleComparison.Compare(clerk, manager);
        differences.Should().Equal(
            new RoleDifference("Gl.Journal", OperationFlags.None, OperationFlags.Post, OperationFlags.None),
            new RoleDifference("Payroll.Employee", OperationFlags.Edit, OperationFlags.Approve, OperationFlags.View));
    }

    [Fact]
    public void Patterns_match_exact_codes_and_prefixes()
    {
        string[] known = ["Payroll.Employee", "Payroll.Approve", "Ar.Invoice"];
        RoleComparison.Matching("payroll.*", known).Should().Equal("Payroll.Approve", "Payroll.Employee");
        RoleComparison.Matching("Ar.Invoice", known).Should().Equal("Ar.Invoice");
        RoleComparison.Matching("New.Code", known).Should().Equal("New.Code");
        RoleComparison.Matching(" ", known).Should().BeEmpty();
    }

    [Fact]
    public void Bulk_changes_add_remove_or_set()
    {
        OperationFlags now = OperationFlags.View | OperationFlags.Edit;
        RoleComparison.Apply(now, OperationFlags.Approve, BulkGrantMode.Add).Should().Be(OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve);
        RoleComparison.Apply(now, OperationFlags.Edit, BulkGrantMode.Remove).Should().Be(OperationFlags.View);
        RoleComparison.Apply(now, OperationFlags.Print, BulkGrantMode.Set).Should().Be(OperationFlags.Print);
    }
}
