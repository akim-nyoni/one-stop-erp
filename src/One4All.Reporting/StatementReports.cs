using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Statements and letters sent to the people the business deals with (brief §14 B, C, I and J): dunning letters, commission statements,
/// the supplier statement, the cash requirement forecast, the ratepayer statement and the student fee statement. Data from
/// <c>StatementReportDataProvider</c> (commands <c>Stmt*</c>).
/// </summary>
public static class StatementReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    // Statement layout shared by the supplier, ratepayer and student statements.
    private static Column[] StatementColumns() =>
    [
        new Column("Account", "Account", 150), new Column("Section", "Section", 110), new Column("Date", "Date", 75), new Column("Type", "Type", 110),
        new Column("Document", "Document", 110), new Column("Debit", "Debit", 85), new Column("Credit", "Credit", 85), new Column("Balance", "Balance", 95, true),
    ];

    /// <summary>All the statement reports.</summary>
    /// <returns>The report definitions.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("AR42", "Dunning / Reminder Letters", "Receivables", "Every customer with overdue debt at the date, at its reminder level (first, second, final), with the "
            + "letter itself — overdue amount after unallocated credits and the oldest days overdue.", "StmtDunning",
            [new Column("Customer", "Customer", 160), new Column("Level", "Level", 40), new Column("Stage", "Stage", 100), new Column("Overdue", "Overdue", 90),
             new Column("OldestDays", "Oldest days", 60), new Column("Letter", "Letter", 400)],
            [("AsAtDate", ParameterType.Date), ("FirstDays", ParameterType.Integer), ("SecondDays", ParameterType.Integer), ("FinalDays", ParameterType.Integer)]);
        yield return Listing("AR46", "Sales Agent Commission Statement & Accrual Schedule", "Receivables", "Per sales rep for the period: commission earned on each customer's base, the "
            + "rep's total to accrue, optionally for one rep.", "StmtCommission",
            [new Column("Rep", "Rep", 80), new Column("Customer", "Customer", 120), new Column("Base", "Commission base", 100), new Column("Commission", "Commission", 100, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("RepCode", ParameterType.String)]);
        yield return Listing("AP51", "Supplier Statement / Remittance Statement", "Purchasing", "Our view of a supplier's account for confirmation: opening balance, every document in the "
            + "period with running balance, closing balance and what is outstanding aged by due date.", "StmtSupplier", StatementColumns(),
            [("SupplierCode", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("AP56", "Cash Requirement Forecast / Payment Due Report", "Purchasing", "What must be paid to suppliers by week of due date and currency — overdue, each of the "
            + "coming weeks and later — net of unallocated payments and debit notes.", "StmtCashRequirement",
            [new Column("Currency", "Currency", 55), new Column("Week", "Week", 80), new Column("From", "From", 75), new Column("To", "To", 75), new Column("Suppliers", "Suppliers", 55),
             new Column("Documents", "Documents", 60), new Column("Amount", "Amount", 100), new Column("Base", "Base", 100, true)],
            [("AsAtDate", ParameterType.Date), ("Weeks", ParameterType.Integer)]);
        yield return Listing("M144", "Consumer / Ratepayer Statement", "Municipal", "Per property: the ratepayer's account with the services connected, opening balance, charges, payments and "
            + "interest in the period, closing balance and ageing; one stand, or every stand for bulk (burst) printing.", "StmtRatepayer", StatementColumns(),
            [("StandNumber", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SC173", "Student Fee Statement", "School", "Per student (and the guardian or sponsor who pays): fees billed by term, payments allocated, arrears brought forward "
            + "and carried, aged; one student or the whole school.", "StmtStudent", StatementColumns(),
            [("AdmissionNumber", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
    }
}
