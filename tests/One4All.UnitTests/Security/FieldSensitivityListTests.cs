using System.Data;
using FluentAssertions;
using One4All.Application.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Thirteenth audit #17 (brief §5.1): desktop lists drop the cost, margin, salary and credit-limit columns a user may not see.</summary>
public sealed class FieldSensitivityListTests
{
    [Fact]
    public void Hidden_kinds_remove_their_columns_from_a_list()
    {
        DataTable table = new();
        foreach (string column in new[] { "Stand", "Cost", "Basic salary", "Account", "Credit limit", "Margin %", "Name" })
        {
            table.Columns.Add(column);
        }

        table.Rows.Add("S1", "1,000.00", "800.00", "1460", "5,000.00", "12.5", "Someone");
        FieldSensitivity.Strip(table, new HashSet<SensitiveField> { SensitiveField.Cost, SensitiveField.Salary }).Should().Be(2);
        table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).Should().Equal("Stand", "Account", "Credit limit", "Margin %", "Name");
        table.Rows[0]["Name"].Should().Be("Someone");

        FieldSensitivity.Strip(table, new HashSet<SensitiveField>()).Should().Be(0, "nothing is hidden from a user with every right");
        FieldSensitivity.Strip(table, new HashSet<SensitiveField> { SensitiveField.CreditLimit, SensitiveField.Margin }).Should().Be(2);
        table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).Should().Equal("Stand", "Account", "Name");
    }
}
