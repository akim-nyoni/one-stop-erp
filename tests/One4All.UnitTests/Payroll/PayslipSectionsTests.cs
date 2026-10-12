using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers the payslip's further sections: contributions, benefits, how it was paid, tax, year to date, leave and loans.</summary>
public sealed class PayslipSectionsTests
{
    [Fact]
    public void Sections_print_only_when_they_have_something_to_show()
    {
        PayslipDocument plain = new() { CompanyName = "Acme", EmployeeName = "Ann", EmployeeNumber = "E1", PeriodLabel = "2026-03", PayDate = new DateOnly(2026, 3, 25), NetPay = 900m };
        PayslipFormatter.ToPlainText(plain).Should().NotContain("TAX YEAR TO DATE").And.NotContain("LEAVE");

        PayslipDocument full = new()
        {
            CompanyName = "Acme", EmployeeName = "Ann", EmployeeNumber = "E1", PeriodLabel = "2026-03", PayDate = new DateOnly(2026, 3, 25), GrossEarnings = 1200m, TotalDeductions = 300m,
            NetPay = 900m, EmployerContributions = [new PayslipItem("Pension (employer)", 90m)], MemoItems = [new PayslipItem("Company car", 150m)], PaidBy = "Paid by bank transfer to CBZ …4321",
            TaxablePay = 1350m, TaxDeducted = 210m, YearToDate = (3600m, 4050m, 630m, 2700m), LeaveBalances = [("ANNUAL", 12.5m)], LoanBalances = [("LN-7", 1500m)],
        };
        string text = PayslipFormatter.ToPlainText(full, 50);
        text.Should().Contain("EMPLOYER CONTRIBUTIONS").And.Contain("Pension (employer)").And.Contain("90.00")
            .And.Contain("TAXABLE BENEFITS (NOT PAID)").And.Contain("Company car")
            .And.Contain("Paid by bank transfer to CBZ …4321")
            .And.Contain("TAX THIS PERIOD").And.Contain("1,350.00").And.Contain("210.00")
            .And.Contain("TAX YEAR TO DATE").And.Contain("4,050.00").And.Contain("630.00")
            .And.Contain("LEAVE BALANCES (DAYS)").And.Contain("12.5")
            .And.Contain("LOAN BALANCES").And.Contain("LN-7").And.Contain("1,500.00");
    }
}
