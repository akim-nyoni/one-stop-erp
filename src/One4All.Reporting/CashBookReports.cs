using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Cash book and treasury reports from brief §14 D not already in the core catalogue (codes are CB plus the brief's item
/// number). Each definition is a listing over a <c>Cash*</c> data command served by the cash book report data provider.
/// </summary>
public static class CashBookReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every CB-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("CB68", "Cash Book Listing", "CashBook", "Every posting to a bank account's ledger in the period with running balance and whether it is reconciled.",
            "CashBookListing", [new Column("Bank", "Bank", 70), new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Document", "Document", 130), new Column("Narrative", "Narrative", 200),
             new Column("Receipts", "Receipts", 90), new Column("Payments", "Payments", 90), new Column("Balance", "Balance", 95), new Column("Reconciled", "Reconciled", 65)],
            [("BankCode", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("CB70", "Unpresented & Uncleared Items Ageing", "CashBook", "Ledger postings not yet on a bank statement and statement lines not yet on the ledger, aged at the date.",
            "CashUnreconciled", [new Column("Bank", "Bank", 70), new Column("Kind", "Kind", 150), new Column("Date", "Date", 75), new Column("Reference", "Reference", 180), new Column("Days", "Days", 45),
             new Column("Band", "Age", 60), new Column("Amount", "Amount", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("CB72", "13-Week Cash Flow Forecast by Currency", "CashBook", "From today's bank balances, week by week: customer receipts and supplier payments falling due, and the closing cash, per currency.",
            "CashForecast", [new Column("Week", "Week starting", 85), new Column("Currency", "Currency", 55), new Column("Opening", "Opening", 100), new Column("Receipts", "Receipts due", 100),
             new Column("Payments", "Payments due", 100), new Column("Closing", "Closing", 100)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("CB73", "Petty Cash & Imprest Report", "CashBook", "Disbursements and top-ups on the petty cash account in the period with its running balance.",
            "CashPetty", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Document", "Document", 120), new Column("Narrative", "Narrative", 220), new Column("In", "Top-ups", 90),
             new Column("Out", "Paid out", 90), new Column("Balance", "Balance", 95)], [("AccountCode", ParameterType.String), ("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("CB74", "Inter-account Transfers", "CashBook", "Transfers between bank accounts in the period, with the amount out, the amount in and any exchange difference.",
            "CashTransfers", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Document", "Document", 110), new Column("From", "From", 80), new Column("To", "To", 80),
             new Column("Out", "Out", 95), new Column("In", "In", 95), new Column("Difference", "FX difference", 95, true)], Period);
    }
}
