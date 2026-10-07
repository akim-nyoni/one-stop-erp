using System.Text.Json.Nodes;
using FluentAssertions;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers recognising restricted fields, who may see them, and stripping them.</summary>
public sealed class FieldSensitivityTests
{
    [Theory]
    [InlineData("averageCost", SensitiveField.Cost)]
    [InlineData("UnitCost", SensitiveField.Cost)]
    [InlineData("grossMarginPercent", SensitiveField.Margin)]
    [InlineData("BasicSalary", SensitiveField.Salary)]
    [InlineData("net_pay", SensitiveField.Salary)]
    [InlineData("GrossEarnings", SensitiveField.Salary)]
    [InlineData("TotalDeductions", SensitiveField.Salary)]
    [InlineData("creditLimit", SensitiveField.CreditLimit)]
    [InlineData("costCentre", null)]
    [InlineData("CostOfSalesAccountCode", null)]
    [InlineData("description", null)]
    public void Names_are_classified(string name, SensitiveField? expected) => FieldSensitivity.Classify(name).Should().Be(expected);

    [Fact]
    public void Rights_decide_what_is_hidden()
    {
        EffectivePermissions clerk = new(new Dictionary<string, OperationFlags> { ["Sales.Customer"] = OperationFlags.View });
        FieldSensitivity.HiddenFor(clerk).Should().BeEquivalentTo([SensitiveField.Cost, SensitiveField.Margin, SensitiveField.Salary, SensitiveField.CreditLimit]);
        EffectivePermissions controller = new(new Dictionary<string, OperationFlags>
        {
            ["Inventory.Item"] = OperationFlags.View | OperationFlags.ViewCost | OperationFlags.ViewMargin, ["Sales.CreditControl"] = OperationFlags.View, ["Payroll.ViewSalary"] = OperationFlags.View,
        });
        FieldSensitivity.HiddenFor(controller).Should().BeEmpty();

        // A line manager who approves leave (or even runs payroll steps) does not see pay without View Salary.
        EffectivePermissions leaveApprover = new(new Dictionary<string, OperationFlags> { ["Payroll.LeaveApprove"] = OperationFlags.View | OperationFlags.Approve, ["Payroll.Calculate"] = OperationFlags.View });
        FieldSensitivity.HiddenFor(leaveApprover).Should().Contain(SensitiveField.Salary);
        FieldSensitivity.HiddenFor(new EffectivePermissions(new Dictionary<string, OperationFlags>(), true)).Should().BeEmpty();
    }

    [Fact]
    public void Hidden_fields_are_stripped_at_any_depth()
    {
        JsonNode json = JsonNode.Parse("""{"items":[{"code":"A","averageCost":2.5,"costCentre":"ADM"}],"customer":{"name":"X","creditLimit":100}}""")!;
        FieldSensitivity.Strip(json, new HashSet<SensitiveField> { SensitiveField.Cost }).Should().Be(1);
        json.ToJsonString().Should().Be("""{"items":[{"code":"A","costCentre":"ADM"}],"customer":{"name":"X","creditLimit":100}}""");

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = [new Dictionary<string, object?> { ["Item"] = "A", ["UnitCost"] = 2m }];
        FieldSensitivity.Strip(rows, new HashSet<SensitiveField> { SensitiveField.Cost }).Single().Keys.Should().Equal("Item");
    }
}
