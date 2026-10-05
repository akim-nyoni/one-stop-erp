using FluentAssertions;
using One4All.Application.Payroll;
using One4All.Core.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public sealed class HrRulesTests
{
    [Theory]
    [InlineData(DisciplinarySanction.VerbalWarning, "2026-07-10")]
    [InlineData(DisciplinarySanction.WrittenWarning, "2027-01-10")]
    [InlineData(DisciplinarySanction.FinalWarning, "2027-01-10")]
    [InlineData(DisciplinarySanction.Suspension, null)]
    [InlineData(DisciplinarySanction.Dismissal, null)]
    public void Warnings_lapse_by_sanction(DisciplinarySanction sanction, string? expected)
        => HrRules.WarningExpiry(sanction, new DateOnly(2026, 1, 10)).Should().Be(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Status_uses_the_window()
    {
        DateOnly asAt = new(2026, 2, 28);
        HrRules.Status(null, asAt, 30).Should().Be("Valid");
        HrRules.Status(new DateOnly(2026, 2, 27), asAt, 30).Should().Be("Expired");
        HrRules.Status(new DateOnly(2026, 3, 30), asAt, 30).Should().Be("Expiring");
        HrRules.Status(new DateOnly(2026, 3, 31), asAt, 30).Should().Be("Valid");
    }

    [Fact]
    public void Required_types_default_and_normalise()
    {
        HrRules.RequiredTypes(null).Should().Equal("ID", "CONTRACT");
        HrRules.RequiredTypes(" id , medical,ID ").Should().Equal("ID", "MEDICAL");
    }
}
