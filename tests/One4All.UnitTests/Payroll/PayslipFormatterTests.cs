using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers the plain-text payslip layout: content, sections, totals and fixed width.</summary>
public sealed class PayslipFormatterTests
{
    private static PayslipDocument Sample() => new()
    {
        CompanyName = "Test Trading Co",
        EmployeeName = "Jo Bloggs",
        EmployeeNumber = "EMP001",
        PeriodLabel = "2026-03",
        PayDate = new DateOnly(2026, 3, 25),
        Earnings = [new PayslipItem("Basic Salary", 1000.00m), new PayslipItem("Housing Allowance", 200.00m)],
        Deductions = [new PayslipItem("PAYE", 150.00m), new PayslipItem("Pension", 50.00m)],
        GrossEarnings = 1200.00m,
        TotalDeductions = 200.00m,
        NetPay = 1000.00m,
    };

    [Fact]
    public void Payslip_contains_sections_items_and_totals()
    {
        string text = PayslipFormatter.ToPlainText(Sample());

        text.Should().Contain("Test Trading Co");
        text.Should().Contain("PAYSLIP");
        text.Should().Contain("Employee: Jo Bloggs");
        text.Should().Contain("EMP001");
        text.Should().Contain("EARNINGS");
        text.Should().Contain("Basic Salary");
        text.Should().Contain("Housing Allowance");
        text.Should().Contain("DEDUCTIONS");
        text.Should().Contain("PAYE");
        text.Should().Contain("NET PAY");
        text.Should().Contain("1,000.00");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 44;
        string text = PayslipFormatter.ToPlainText(Sample(), width);

        foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            line.Length.Should().BeLessThanOrEqualTo(width);
        }
    }

    [Fact]
    public void Net_pay_is_right_aligned_against_its_label()
    {
        string text = PayslipFormatter.ToPlainText(Sample(), 44);
        string netLine = text.Split('\n').Select(l => l.TrimEnd('\r')).First(l => l.StartsWith("NET PAY", StringComparison.Ordinal));

        netLine.Should().EndWith("1,000.00");
        netLine.Length.Should().Be(44);
    }
}
