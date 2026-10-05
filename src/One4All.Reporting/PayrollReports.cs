using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Payroll &amp; HR reports from brief §14 O (the PR codes) that today's payroll data supports, over <c>Pyrl*</c> data
/// commands served by the payroll report data provider. Payslips count in the period their pay date falls in.
/// </summary>
public static class PayrollReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every PR-coded definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("PR2", "Payroll Summary by Element", "Payroll", "Totals for the period by earning, deduction and employer contribution, with how many employees each.",
            "PyrlSummary", [new Column("Type", "Type", 110), new Column("Element", "Element", 80), new Column("Description", "Description", 160), new Column("Employees", "Employees", 70), new Column("Amount", "Amount", 100, true)], Period);
        yield return Listing("PR3", "Payroll Journal", "Payroll", "The payroll postings of the period by account, with debits and credits.",
            "PyrlJournal", [new Column("Journal", "Journal", 100), new Column("Date", "Date", 75), new Column("Account", "Account", 70), new Column("Name", "Name", 160), new Column("Debit", "Debit", 95),
             new Column("Credit", "Credit", 95), new Column("Net", "Net (Dr +)", 95, true)], Period);
        yield return Listing("PR5", "Payroll Variance / Movement", "Payroll", "Each employee's net pay this pay month against last, with starters, leavers and changes over 10% flagged.",
            "PyrlVariance", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Previous", "Last month", 95), new Column("Current", "This month", 95),
             new Column("Flag", "Flag", 110), new Column("Change", "Change", 95, true)], Period);
        yield return Listing("PR6", "Payroll Calculation Trace", "Payroll", "Every payslip line in the period with how its element is calculated and the employee's basic salary.",
            "PyrlTrace", [new Column("Employee", "Employee", 150), new Column("Period", "Period", 70), new Column("Element", "Element", 70), new Column("Basis", "Basis", 100), new Column("Rule", "Rate / formula", 150),
             new Column("Basic", "Basic", 85), new Column("Amount", "Amount", 95, true)], Period);
        yield return Listing("PR12", "Cash Pay Denomination Breakdown", "Payroll", "Net pay in the period broken into the base currency's notes and coins, per employee, for cash payouts.",
            "PyrlDenominations", [new Column("Employee", "Employee", 160), new Column("Breakdown", "Notes & coins", 300), new Column("Net", "Net pay", 95, true)], Period);
        yield return Listing("PR16", "Net Pay Control Reconciliation", "Payroll", "Per pay month: gross, deductions and net from the payslips against the net pay credited in the payroll journals.",
            "PyrlNetControl", [new Column("Month", "Month", 70), new Column("Payslips", "Payslips", 60), new Column("Gross", "Gross", 95), new Column("Deductions", "Deductions", 95), new Column("Net", "Net per payslips", 100),
             new Column("Journal", "Net per journal", 100), new Column("Difference", "Difference", 95, true)], Period);
        yield return Listing("PR17", "Earnings Analysis", "Payroll", "Earnings in the period by department and element.",
            "PyrlEarnings", [new Column("Department", "Department", 100), new Column("Element", "Element", 80), new Column("Description", "Description", 160), new Column("Employees", "Employees", 70),
             new Column("Amount", "Amount", 100, true)], Period);
        yield return Listing("PR29", "Tax Reconciliation", "Payroll", "Per employee over the period: taxable earnings, PAYE deducted and the effective rate.",
            "PyrlTax", [new Column("Employee", "Employee", 160), new Column("Taxable", "Taxable earnings", 100), new Column("Rate", "Effective rate", 80), new Column("Tax", "Tax deducted", 100, true)], Period);
        yield return Listing("PR38", "Leave Liability (IAS 19)", "Payroll", "Leave days owed at the date per employee and type, valued at the daily rate of basic salary (annual ÷ 260).",
            "PyrlLeave", [new Column("Employee", "Employee", 160), new Column("LeaveType", "Leave", 90), new Column("Accrued", "Accrued", 65), new Column("Taken", "Taken", 60), new Column("Balance", "Balance", 65),
             new Column("DailyRate", "Daily rate", 80), new Column("Liability", "Liability", 100, true)], []);
        yield return Listing("PR49", "Salary Change Audit", "Payroll", "Every change to an employee's basic salary in the period, with who changed it.",
            "PyrlSalaryChanges", [new Column("When", "When", 115), new Column("Employee", "Employee", 160), new Column("User", "Changed by", 90), new Column("Old", "From", 95), new Column("New", "To", 95)], Period);
        yield return Listing("PR59", "Labour Cost by Department & Cost Centre", "Payroll", "Gross pay plus employer contributions in the period by department and cost centre.",
            "PyrlLabourCost", [new Column("Department", "Department", 100), new Column("CostCentre", "Cost centre", 90), new Column("Employees", "Employees", 70), new Column("Gross", "Gross", 95),
             new Column("Employer", "Employer contributions", 110), new Column("Cost", "Total cost", 100, true)], Period);
        yield return Listing("PR60", "Payroll Cost vs Budget", "Payroll", "Year to date on each payroll expense account: budget, actual and the variance.",
            "PyrlBudget", [new Column("Account", "Account", 70), new Column("Name", "Name", 160), new Column("Budget", "Budget YTD", 95), new Column("Actual", "Actual YTD", 95), new Column("Variance", "Under / (over)", 100, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR61", "Payroll as % of Revenue", "Payroll", "Month by month: payroll cost (gross + employer contributions) against revenue.",
            "PyrlRevenueShare", [new Column("Month", "Month", 70), new Column("Revenue", "Revenue", 100), new Column("Share", "Payroll %", 70), new Column("Payroll", "Payroll cost", 100, true)], Period);
        yield return Listing("PR64", "Payroll GL Reconciliation", "Payroll", "Per element account: what the payslips say against what the ledger holds for the period.",
            "PyrlGlRecon", [new Column("Account", "Account", 70), new Column("Name", "Name", 160), new Column("Payslips", "Per payslips", 100), new Column("Ledger", "Per ledger", 100), new Column("Difference", "Difference", 95, true)], Period);
        yield return Listing("PR65", "Payroll Audit Trail", "Payroll", "Every recorded change in the period to employees, elements, assignments, loans, leave and payslips.",
            "PyrlAudit", [new Column("When", "When", 115), new Column("User", "User", 90), new Column("Entity", "Record", 130), new Column("Id", "Id", 60), new Column("Action", "Action", 60),
             new Column("Changed", "Fields changed", 260)], Period);
        yield return Listing("PR67", "Payroll Report Access & Export Log", "Payroll", "Every run of a payroll report in the period — who, what, format and copy.",
            "PyrlAccessLog", [new Column("When", "When", 115), new Column("Report", "Report", 80), new Column("User", "User", 90), new Column("Purpose", "Purpose", 70), new Column("Format", "Format", 55),
             new Column("Copy", "Copy", 45)], Period);
        yield return Listing("PR70", "Year-to-Date Payroll Register", "Payroll", "Per employee for the year to the end of the period: gross, taxable, deductions, employer contributions and net.",
            "PyrlYtd", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Gross", "Gross", 95), new Column("Taxable", "Taxable", 95), new Column("Deductions", "Deductions", 95),
             new Column("Employer", "Employer", 90), new Column("Net", "Net pay", 100, true)], Period);
    }
}
