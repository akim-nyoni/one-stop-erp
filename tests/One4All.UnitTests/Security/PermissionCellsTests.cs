using FluentAssertions;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers the permission grid's cells: every operation flag is a column, and a user's cell grants, denies or inherits.</summary>
public sealed class PermissionCellsTests
{
    [Fact]
    public void Every_flag_is_a_column_and_cells_round_trip_to_grant_and_deny_overrides()
    {
        PermissionCells.Columns.Should().HaveCount(21).And.NotContain(OperationFlags.None).And.Contain(OperationFlags.BackDate);
        PermissionCells.CellOf(OperationFlags.Print, OperationFlags.Print, OperationFlags.Print).Should().BeFalse("deny wins");
        PermissionCells.CellOf(OperationFlags.Export, OperationFlags.Export, OperationFlags.None).Should().BeTrue();
        PermissionCells.CellOf(OperationFlags.View, OperationFlags.Export, OperationFlags.Print).Should().BeNull();

        bool?[] cells = new bool?[PermissionCells.Columns.Length];
        cells[Array.IndexOf(PermissionCells.Columns, OperationFlags.Export)] = true;
        cells[Array.IndexOf(PermissionCells.Columns, OperationFlags.Print)] = false;
        cells[Array.IndexOf(PermissionCells.Columns, OperationFlags.BackDate)] = true;
        (OperationFlags grant, OperationFlags deny) = PermissionCells.FromCells(cells, OperationFlags.View | OperationFlags.Print | OperationFlags.Export);
        grant.Should().Be(OperationFlags.Export, "back-dating is not offered by the feature");
        deny.Should().Be(OperationFlags.Print);
        PermissionCells.Effective(OperationFlags.View | OperationFlags.Print, grant, deny).Should().Be(OperationFlags.View | OperationFlags.Export);
    }
}
