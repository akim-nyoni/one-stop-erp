using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Thirteenth audit #23: the variable import reads the template, named columns in any order, and a sheet with a column per element.</summary>
public sealed class PayrollVariableCsvTests
{
    private static readonly HashSet<string> Elements = ["OVERTIME", "BONUS", "ABSENCE"];

    private static IReadOnlyList<ParsedVariable> Read(string csv)
        => PayrollVariableCsv.Parse(csv, Elements.Contains, code => code is "OVERTIME" or "ABSENCE", number => number.StartsWith("E", StringComparison.Ordinal));

    [Fact]
    public void The_template_reads_as_before_with_or_without_its_header()
    {
        Read("Employee number,Element code,Units,Amount\nE1,overtime,10,\nE2,BONUS,,500").Select(v => (v.Line, v.EmployeeNumber, v.ElementCode, v.Units, v.Amount))
            .Should().Equal((2, "E1", "OVERTIME", 10m, (decimal?)null), (3, "E2", "BONUS", (decimal?)null, 500m));
        Read("E1,OVERTIME,10,\nE2,BONUS,,500").Should().HaveCount(2, "a first line that is data is kept");
    }

    [Fact]
    public void Named_columns_may_come_in_any_order_and_quotes_hold_the_separator()
    {
        IReadOnlyList<ParsedVariable> read = Read("Amount,Name,Code,Employee\n\"1,250.50\",Tendai Moyo,bonus,E7\r\n,Rudo,OVERTIME,E8");
        read.Select(v => (v.EmployeeNumber, v.ElementCode, v.Amount)).Should().Equal(("E7", "BONUS", (decimal?)1_250.50m), ("E8", "OVERTIME", (decimal?)null));
        Read("Employee;Element;Hours\nE1;OVERTIME;7,5").Single().Error.Should().Be("Units and amount must be numbers.", "a decimal comma is not read as a number");
    }

    [Fact]
    public void A_sheet_gives_a_variable_per_filled_cell()
    {
        IReadOnlyList<ParsedVariable> read = Read("Employee\tName\tOvertime\tBonus\tCanteen\nE1\tTendai\t12\t\t\nE2\tRudo\t\t300\tx");
        read.Should().ContainSingle(v => v.Error == "Column 'Canteen' is not a pay element." && v.Line == 1);
        read.Where(v => v.Error is null).Select(v => (v.Line, v.EmployeeNumber, v.ElementCode, v.Units, v.Amount))
            .Should().Equal((2, "E1", "OVERTIME", (decimal?)12m, (decimal?)null), (3, "E2", "BONUS", (decimal?)null, (decimal?)300m));
        Read("Employee,Bonus\nE1,lots").Single().Error.Should().Be("'lots' under BONUS is not a number.");
    }
}
