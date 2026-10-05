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
        yield return Listing("PR26", "Allowance Analysis", "Payroll", "Earnings other than basic salary in the period, per employee and allowance.",
            "PyrlAllowances", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Element", "Allowance", 80), new Column("Description", "Description", 150),
             new Column("Taxable", "Taxable", 55), new Column("Amount", "Amount", 95, true)], Period);
        yield return Listing("PR29", "Tax Reconciliation", "Payroll", "Per employee over the period: taxable earnings, PAYE deducted and the effective rate.",
            "PyrlTax", [new Column("Employee", "Employee", 160), new Column("Taxable", "Taxable earnings", 100), new Column("Rate", "Effective rate", 80), new Column("Tax", "Tax deducted", 100, true)], Period);
        yield return Listing("PR38", "Leave Liability (IAS 19)", "Payroll", "Leave days owed at the date per employee and type, valued at the daily rate of basic salary (annual ÷ 260).",
            "PyrlLeave", [new Column("Employee", "Employee", 160), new Column("LeaveType", "Leave", 90), new Column("Accrued", "Accrued", 65), new Column("Taken", "Taken", 60), new Column("Balance", "Balance", 65),
             new Column("DailyRate", "Daily rate", 80), new Column("Liability", "Liability", 100, true)], []);
        yield return Listing("PR49", "Salary Change Audit", "Payroll", "Every change to an employee's basic salary in the period, with who changed it.",
            "PyrlSalaryChanges", [new Column("When", "When", 115), new Column("Employee", "Employee", 160), new Column("User", "Changed by", 90), new Column("Old", "From", 95), new Column("New", "To", 95)], Period);
        yield return Listing("PR58", "Total Cost to Company", "Payroll", "Per employee over the period: gross pay plus employer contributions, and the annualised cost.",
            "PyrlCostToCompany", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Months", "Months paid", 65), new Column("Gross", "Gross", 95),
             new Column("Employer", "Employer contributions", 110), new Column("Annualised", "Annualised", 100), new Column("Cost", "Cost in period", 100, true)], Period);
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

        // Employee master data.
        yield return Listing("PR4", "Pre-Payroll Validation", "Payroll", "Active employees whose records would stop a clean payment or return: missing ID, tax number, bank or wallet details, date of birth.",
            "PyrlValidation", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Method", "Paid by", 90), new Column("Issues", "Issues", 340)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR11", "Bank Transfer (EFT) Schedule", "Payroll", "Net pay for the period to employees paid by bank transfer (held salaries excluded), with bank, branch and account.",
            "PyrlEft", [new Column("Employee", "Employee", 160), new Column("Bank", "Bank", 110), new Column("Branch", "Branch", 60), new Column("Account", "Account", 110), new Column("Reference", "Reference", 80),
             new Column("Net", "Net pay", 100, true)], Period);
        yield return Listing("PR13", "Mobile Money Payment Schedule", "Payroll", "Net pay for the period to employees paid by mobile money (held salaries excluded), with the wallet.",
            "PyrlMobile", [new Column("Employee", "Employee", 160), new Column("Wallet", "Wallet", 110), new Column("Reference", "Reference", 80), new Column("Net", "Net pay", 100, true)], Period);
        yield return Listing("PR15", "Held Salaries", "Payroll", "Employees whose pay is held back, why, and the net pay held for the period.",
            "PyrlHeld", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Method", "Paid by", 90), new Column("Reason", "Reason", 220), new Column("Net", "Net held", 100, true)], Period);
        yield return Listing("PR45", "Headcount Reconciliation", "Payroll", "Per department over the period: opening headcount, starters, leavers and closing.",
            "PyrlHeadcount", [new Column("Department", "Department", 110), new Column("Opening", "Opening", 60), new Column("Starters", "Starters", 60), new Column("Leavers", "Leavers", 60), new Column("Closing", "Closing", 60)], Period);
        yield return Listing("PR46", "Starters & Leavers", "Payroll", "Employees engaged or leaving in the period, with the date and the reason for leaving.",
            "PyrlStartersLeavers", [new Column("Kind", "Kind", 70), new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Date", "Date", 75), new Column("Reason", "Reason", 200),
             new Column("Basic", "Basic salary", 95, true)], Period);
        yield return Listing("PR48", "Grade & Gender Distribution", "Payroll", "Active headcount per grade with the gender split and the basic salary range.",
            "PyrlGrades", [new Column("Grade", "Grade", 70), new Column("Headcount", "Headcount", 65), new Column("Female", "Female", 55), new Column("Male", "Male", 50), new Column("Unspecified", "Not recorded", 75),
             new Column("Range", "Basic range", 150), new Column("Average", "Average basic", 100, true)], []);
        yield return Listing("PR50", "Employee Bank Detail Change Audit", "Payroll", "Every change to how or where an employee is paid, with who changed it (account numbers masked).",
            "PyrlBankChanges", [new Column("When", "When", 115), new Column("Employee", "Employee", 160), new Column("User", "Changed by", 90), new Column("Changed", "Fields", 180), new Column("New", "New details", 200)], Period);
        yield return Listing("PR51", "Contract, Probation & Permit Expiry", "Payroll", "Fixed-term contracts, probations and work permits ending within 90 days of the date, or already passed.",
            "PyrlExpiries", [new Column("Kind", "Kind", 90), new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Date", "Date", 75), new Column("Due", "Days", 110)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR68", "Ghost Worker Investigation Pack", "Payroll", "Active employees sharing a bank account, wallet or national ID with another, with no ID, or with no leave in over a year.",
            "PyrlGhosts", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 90), new Column("Engaged", "Engaged", 75), new Column("Signals", "Signals", 360)], [("AsAtDate", ParameterType.Date)]);

        // Runs, schemes and tax.
        yield return Listing("PR7", "Payroll Run Control Register", "Payroll", "Every payroll run in the period: employees, gross, deductions, net, the journal and who ran it.",
            "PyrlRuns", [new Column("Period", "Period", 70), new Column("PayDate", "Pay date", 75), new Column("Employees", "Employees", 65), new Column("Gross", "Gross", 95), new Column("Deductions", "Deductions", 95),
             new Column("Journal", "Journal", 100), new Column("RunBy", "Run by", 90), new Column("Net", "Net", 100, true)], Period);
        yield return Listing("PR19", "Third-Party Remittance Schedules", "Payroll", "Per scheme and member for the period: the employee's deduction and the employer's contribution — the schedule each provider needs.",
            "PyrlSchemes", [new Column("Scheme", "Scheme", 110), new Column("Employee", "Employee", 160), new Column("Member", "Member no.", 90), new Column("EmployeePart", "Employee", 90),
             new Column("EmployerPart", "Employer", 90), new Column("Total", "Total", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("SchemeCode", ParameterType.String)]);
        yield return Listing("PR20", "Deductions vs Remittances", "Payroll", "Per scheme and period: what the payroll deducted and contributed against what was paid over, with the gap.",
            "PyrlRemittances", [new Column("Scheme", "Scheme", 120), new Column("Period", "Period", 70), new Column("Deducted", "Deducted + contributed", 120), new Column("Remitted", "Remitted", 100),
             new Column("Status", "Status", 90), new Column("Outstanding", "Outstanding", 100, true)], Period);
        yield return Listing("PR27", "PAYE Remittance Return", "Payroll", "Per month: employees, taxable earnings, PAYE deducted and PAYE by the tax table in force, with any difference.",
            "PyrlPayeReturn", [new Column("Month", "Month", 70), new Column("Employees", "Employees", 65), new Column("Taxable", "Taxable", 100), new Column("ByTable", "Per tax table", 100), new Column("Difference", "Difference", 90),
             new Column("Deducted", "PAYE deducted", 100, true)], Period);
        yield return Listing("PR28", "Annual Tax Certificates", "Payroll", "Per employee for the year: ID and tax numbers, gross, taxable, pension contributions and PAYE — the certificate data.",
            "PyrlCertificates", [new Column("Employee", "Employee", 160), new Column("NationalId", "National ID", 100), new Column("TaxNumber", "Tax no.", 80), new Column("Gross", "Gross", 95), new Column("Taxable", "Taxable", 95),
             new Column("Pension", "Pension", 85), new Column("Paye", "PAYE", 95, true)], Period);
        yield return Listing("PR30", "Social Security Contribution Schedule", "Payroll", "Social security members with the period's employee and employer contributions.",
            "PyrlSchemeSocial", [new Column("Scheme", "Scheme", 110), new Column("Employee", "Employee", 160), new Column("Member", "Member no.", 90), new Column("EmployeePart", "Employee", 90),
             new Column("EmployerPart", "Employer", 90), new Column("Total", "Total", 95, true)], Period);
        yield return Listing("PR31", "Pension Fund Contribution Schedule", "Payroll", "Pension fund members with the period's employee and employer contributions.",
            "PyrlSchemePension", [new Column("Scheme", "Scheme", 110), new Column("Employee", "Employee", 160), new Column("Member", "Member no.", 90), new Column("EmployeePart", "Employee", 90),
             new Column("EmployerPart", "Employer", 90), new Column("Total", "Total", 95, true)], Period);
        yield return Listing("PR32", "Medical Aid Contribution Schedule", "Payroll", "Medical aid members with the period's employee and employer contributions.",
            "PyrlSchemeMedical", [new Column("Scheme", "Scheme", 110), new Column("Employee", "Employee", 160), new Column("Member", "Member no.", 90), new Column("EmployeePart", "Employee", 90),
             new Column("EmployerPart", "Employer", 90), new Column("Total", "Total", 95, true)], Period);
        yield return Listing("PR33", "Workers' Compensation & Skills Levy Schedule", "Payroll", "Workers' compensation and skills levy contributions for the period by member.",
            "PyrlSchemeLevy", [new Column("Scheme", "Scheme", 110), new Column("Employee", "Employee", 160), new Column("Member", "Member no.", 90), new Column("EmployeePart", "Employee", 90),
             new Column("EmployerPart", "Employer", 90), new Column("Total", "Total", 95, true)], Period);
        yield return Listing("PR36", "Tax Table in Force", "Payroll", "The PAYE bands that apply at the date, with the tax at the top of each band.",
            "PyrlTaxTable", [new Column("Table", "Table", 80), new Column("Effective", "Effective", 75), new Column("From", "From", 90), new Column("To", "To", 90), new Column("Rate", "Rate %", 60),
             new Column("Cumulative", "Tax at band top", 100)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR14", "Split-Pay Report", "Payroll", "Employees whose net pay is split across accounts or methods: each fixed part and what is left for the primary account from the latest net pay in the period.",
            "PyrlSplitPay", [new Column("Employee", "Employee", 160), new Column("Primary", "Primary", 140), new Column("Splits", "Other parts", 240), new Column("Net", "Net pay", 90),
             new Column("Remainder", "To primary", 95, true)], Period);
        yield return Listing("PR21", "Garnishee & Court Order Register", "Payroll", "Every court order by employee and priority: creditor, instalment, total ordered, deducted through payroll to the date and the balance still to collect.",
            "PyrlGarnishees", [new Column("Employee", "Employee", 150), new Column("Reference", "Order", 90), new Column("Creditor", "Creditor", 130), new Column("Priority", "Priority", 50), new Column("Issued", "Issued", 75),
             new Column("Instalment", "Instalment", 80), new Column("Total", "Ordered", 90), new Column("Deducted", "Deducted", 90), new Column("Balance", "Balance", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR22", "Loan & Advance Register and Balances", "Payroll", "Every employee loan and advance: granted, principal, instalment, repaid, instalments left, status and the balance outstanding.",
            "PyrlLoans", [new Column("Employee", "Employee", 160), new Column("Reference", "Reference", 90), new Column("Granted", "Granted", 75), new Column("Principal", "Principal", 90),
             new Column("Instalment", "Instalment", 80), new Column("Repaid", "Repaid", 90), new Column("Left", "Instalments left", 70), new Column("Status", "Status", 70), new Column("Outstanding", "Outstanding", 95, true)], []);
        yield return Listing("PR25", "Overtime Analysis", "Payroll", "Overtime in the period by department, employee and multiplier: hours, approved and unapproved hours, and the cost at the basic hourly rate.",
            "PyrlOvertime", [new Column("Department", "Department", 100), new Column("Employee", "Employee", 150), new Column("Multiplier", "Rate ×", 50), new Column("Hours", "Hours", 55), new Column("Approved", "Approved", 60),
             new Column("Unapproved", "Unapproved", 70), new Column("Cost", "Cost", 90, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("HoursPerMonth", ParameterType.Decimal)]);
        yield return Listing("PR34", "Taxable Benefits Schedule", "Payroll", "Benefits in kind paid in the period per employee: category, value and the PAYE they add at the table in force.",
            "PyrlBenefits", [new Column("Employee", "Employee", 160), new Column("Category", "Category", 110), new Column("Element", "Element", 120), new Column("Value", "Value", 90),
             new Column("TaxEffect", "Tax effect", 90, true)], Period);
        yield return Listing("PR39", "Leave Taken Analysis", "Payroll", "Leave taken in the period by type, department and month: employees and days.",
            "PyrlLeaveTaken", [new Column("Type", "Type", 80), new Column("Department", "Department", 110), new Column("Month", "Month", 65), new Column("Employees", "Employees", 65), new Column("Records", "Absences", 60),
             new Column("Days", "Days", 70, true)], Period);
        yield return Listing("PR40", "Absence, Lateness & Sick Leave Pattern", "Payroll", "Per employee over the period: absent days, late days and minutes, sick days (Mondays / Fridays) and where they stand on sick pay over the last 12 months.",
            "PyrlAbsence", [new Column("Employee", "Employee", 150), new Column("Absent", "Absent", 50), new Column("Late", "Late days", 55), new Column("LateMinutes", "Late min", 55), new Column("Sick", "Sick", 45),
             new Column("MonFri", "Mon / Fri sick", 70), new Column("Rolling", "Sick (12 mo)", 70), new Column("Tier", "Sick pay", 70)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("FullPayDays", ParameterType.Decimal), ("HalfPayDays", ParameterType.Decimal)]);
        yield return Listing("PR41", "Leave Encashment Report", "Payroll", "Leave paid out in the period instead of taken: employee, type, days and amount.",
            "PyrlEncashment", [new Column("Date", "Date", 75), new Column("Employee", "Employee", 160), new Column("Type", "Type", 80), new Column("Days", "Days", 50), new Column("Rate", "Daily rate", 80),
             new Column("Amount", "Amount", 95, true)], Period);
        yield return Listing("PR42", "Timesheet Hours & Labour Cost Allocated to Jobs", "Payroll", "Per employee over the period: hours and cost booked to jobs against gross pay, and the pay left unallocated.",
            "PyrlJobAllocation", [new Column("Employee", "Employee", 160), new Column("Jobs", "Jobs", 140), new Column("Hours", "Hours", 55), new Column("Allocated", "Allocated", 90), new Column("Gross", "Gross pay", 90),
             new Column("Share", "Allocated %", 70), new Column("Unallocated", "Unallocated", 95, true)], Period);
        yield return Listing("PR43", "Attendance Exception Report", "Payroll", "Days in the period with no clock-in or clock-out, unapproved overtime, or hours over the daily maximum.",
            "PyrlAttendanceExceptions", [new Column("Date", "Date", 75), new Column("Employee", "Employee", 160), new Column("In", "In", 50), new Column("Out", "Out", 50), new Column("Hours", "Hours", 50),
             new Column("Exception", "Exception", 240)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("MaxHours", ParameterType.Decimal)]);
        yield return Listing("PR47", "Establishment vs Actual", "Payroll", "Per department and grade at the date: approved posts, filled, vacant and over establishment, with the annual saving on vacant posts.",
            "PyrlEstablishment", [new Column("Department", "Department", 110), new Column("Grade", "Grade", 60), new Column("Approved", "Approved", 60), new Column("Filled", "Filled", 50), new Column("Vacant", "Vacant", 50),
             new Column("Over", "Over", 45), new Column("Saving", "Vacancy saving", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR52", "Termination & Final Settlement", "Payroll", "Leavers in the period with their final settlement computed: notice, leave, gratuity, severance (retrenchment), recoveries, tax on terminal benefits and the net.",
            "PyrlFinalSettlement", [new Column("Employee", "Employee", 150), new Column("Left", "Left", 75), new Column("Reason", "Reason", 90), new Column("Years", "Years", 40), new Column("Notice", "Notice", 75),
             new Column("Leave", "Leave", 75), new Column("Gratuity", "Gratuity", 75), new Column("Severance", "Severance", 75), new Column("Recoveries", "Recoveries", 75), new Column("Tax", "Tax", 65),
             new Column("Net", "Net", 90, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("NoticeMonths", ParameterType.Decimal), ("GratuityWeeksPerYear", ParameterType.Decimal), ("SeveranceWeeksPerYear", ParameterType.Decimal),
             ("TerminalTaxRate", ParameterType.Decimal)]);
        yield return Listing("PR53", "Clearance Checklist Outstanding", "Payroll", "Leavers by the date who still hold company assets, owe on loans or have leave not yet settled.",
            "PyrlClearance", [new Column("Employee", "Employee", 160), new Column("Left", "Left", 75), new Column("Reason", "Reason", 110), new Column("Assets", "Assets held", 170), new Column("Leave", "Leave days", 60),
             new Column("Items", "Outstanding", 60), new Column("Loans", "Loans owed", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR54", "Gratuity & Long Service Provision", "Payroll", "Per employee in service: completed years, the gratuity earned to date and its value discounted from normal retirement (IAS 19 other long-term benefits).",
            "PyrlGratuity", [new Column("Employee", "Employee", 160), new Column("Years", "Service years", 70), new Column("Accrued", "Earned to date", 95), new Column("ToRetirement", "Years to retirement", 90),
             new Column("Provision", "Provision", 95, true)],
            [("AsAtDate", ParameterType.Date), ("WeeksPerYear", ParameterType.Decimal), ("RetirementAge", ParameterType.Integer), ("DiscountRate", ParameterType.Decimal)]);
        yield return Listing("PR55", "Employee Documents Expiring / Missing", "Payroll", "Employees in service whose documents have expired or expire within the window, or who lack a required document type.",
            "PyrlDocuments", [new Column("Employee", "Employee", 160), new Column("Type", "Document", 90), new Column("Reference", "Number", 100), new Column("Expires", "Expires", 75), new Column("Status", "Status", 80)],
            [("AsAtDate", ParameterType.Date), ("Days", ParameterType.Integer), ("RequiredTypes", ParameterType.String)]);
        yield return Listing("PR56", "Disciplinary & Warning Register", "Payroll", "Disciplinary records with the sanction, when warnings lapse and whether they are still live at the date.",
            "PyrlDisciplinary", [new Column("Employee", "Employee", 160), new Column("Date", "Date", 75), new Column("Offence", "Offence", 180), new Column("Sanction", "Sanction", 100), new Column("Expires", "Lapses", 75),
             new Column("Status", "Status", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR57", "Training & Qualification Register; Appraisal Due", "Payroll", "Training and qualifications held with renewal status, then each employee's last appraisal and when the next is due.",
            "PyrlTraining", [new Column("Section", "Section", 70), new Column("Employee", "Employee", 160), new Column("Item", "Course / rating", 160), new Column("Date", "Completed / held", 80),
             new Column("Due", "Renew / next due", 80), new Column("Status", "Status", 70), new Column("Cost", "Cost", 85, true)], [("AsAtDate", ParameterType.Date), ("Days", ParameterType.Integer)]);
        yield return Listing("PR62", "Overtime & Casual Labour vs Permanent Cost", "Payroll", "Per department over the period: pay to permanent and contract staff, to casuals, and overtime, with casual plus overtime as a share of the permanent cost.",
            "PyrlLabourMix", [new Column("Department", "Department", 110), new Column("Permanent", "Permanent & contract", 110), new Column("Casual", "Casual", 90), new Column("Overtime", "Overtime", 90),
             new Column("Share", "Casual + OT / permanent", 110), new Column("Total", "Total labour", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("HoursPerMonth", ParameterType.Decimal)]);
        yield return Listing("PR63", "Payroll Accrual Schedule", "Payroll", "Employees in service at the period end not yet paid for that month (including salaries on hold), with the basic salary to accrue.",
            "PyrlAccrual", [new Column("Employee", "Employee", 160), new Column("Department", "Department", 100), new Column("LastPaid", "Last paid", 75), new Column("Status", "Status", 140),
             new Column("Accrual", "Accrual", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("PR66", "Segregation of Duties Violations — Payroll", "Payroll", "Users who processed a payroll run in the period after changing employee pay or payment details, and anyone who processed a run that paid themselves.",
            "PyrlSod", [new Column("User", "User", 100), new Column("Violation", "Violation", 200), new Column("Detail", "Detail", 260), new Column("Run", "Run", 100), new Column("When", "When", 110)], Period);
        yield return Listing("PR69", "Payslip Reprint Log", "Payroll", "Payslips printed more than once: each print after the first with when and by whom.",
            "PyrlReprints", [new Column("Employee", "Employee", 160), new Column("Period", "Period", 70), new Column("Print", "Print no.", 55), new Column("When", "When", 115), new Column("User", "By", 100)], Period);
    }
}
