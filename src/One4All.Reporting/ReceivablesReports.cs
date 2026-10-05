using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Accounts Receivable reports from brief §14 B not already in the core catalogue (codes are AR plus the brief's item
/// number). Each definition is a listing over a <c>Recv*</c> data command served by the receivables report data provider.
/// </summary>
public static class ReceivablesReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every AR-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("AR29", "Aged Debtors Detail", "Sales", "Every open item as at the date with its age from due date (or invoice date for customers aged that way) and band.",
            "RecvAgedDetail", [new Column("Customer", "Customer", 150), new Column("Document", "Document", 100), new Column("Type", "Type", 80), new Column("Date", "Date", 75), new Column("Due", "Due", 75),
             new Column("Days", "Days", 50), new Column("Band", "Band", 60), new Column("Outstanding", "Outstanding", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR30", "Aged Debtors by Sales Rep and Currency", "Sales", "Debtors aged as at the date, grouped by sales rep (or agent) and currency, in base.",
            "RecvAgedByRep", [new Column("Rep", "Sales rep", 120), new Column("Currency", "Currency", 60), new Column("Customers", "Customers", 60), new Column("Current", "Current", 85),
             new Column("D30", "1–30", 85), new Column("D60", "31–60", 85), new Column("D90", "61–90", 85), new Column("D120", "91–120", 85), new Column("Over120", "120+", 85), new Column("Total", "Total", 95, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR31", "Customer Ledger", "Sales", "One customer's documents in date order with running balance and what is still outstanding on each.",
            "RecvLedger", [new Column("Date", "Date", 75), new Column("Type", "Type", 80), new Column("Document", "Document", 110), new Column("Due", "Due", 75), new Column("Debit", "Debit", 90),
             new Column("Credit", "Credit", 90), new Column("Balance", "Balance", 95), new Column("Outstanding", "Outstanding", 95, true)], [("CustomerCode", ParameterType.String)]);
        yield return Listing("AR33", "Sales Day Book", "Sales", "Invoices, credit notes and debit notes raised in the period, in transaction and base currency.",
            "RecvDayBook", [new Column("Date", "Date", 75), new Column("Document", "Document", 110), new Column("Type", "Type", 80), new Column("Customer", "Customer", 150), new Column("Currency", "Currency", 55),
             new Column("Transaction", "Transaction", 95), new Column("Base", "Base", 95, true)], Period);
        yield return Listing("AR34", "Sales Analysis with Margin", "Sales", "Invoiced order lines and POS sales in the period by customer and item, against current average cost.",
            "RecvSalesAnalysis", [new Column("Customer", "Customer", 140), new Column("Item", "Item", 150), new Column("Quantity", "Qty", 60), new Column("Sales", "Net sales", 95), new Column("Cost", "Cost", 90),
             new Column("MarginPercent", "Margin %", 60), new Column("Margin", "Margin", 95, true)], Period);
        yield return Listing("AR35", "Top Customers (Pareto)", "Sales", "Customers ranked by net sales in the period, with share and cumulative share.",
            "RecvTopCustomers", [new Column("Rank", "Rank", 40), new Column("Customer", "Customer", 160), new Column("Documents", "Documents", 65), new Column("Share", "Share", 60),
             new Column("Cumulative", "Cumulative", 70), new Column("Sales", "Net sales", 100, true)], Period);
        yield return Listing("AR36", "Customer Sales History & Trend", "Sales", "Per customer: net sales this year to date against the same period last year, and the last three months against the three before.",
            "RecvSalesTrend", [new Column("Customer", "Customer", 160), new Column("LastYear", "Same period last year", 110), new Column("Change", "Change", 65), new Column("Last3", "Last 3 months", 95),
             new Column("Prior3", "Prior 3 months", 95), new Column("Trend", "Trend", 60), new Column("ThisYear", "This year to date", 105, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR38", "Receipts / Collections Report", "Receivables", "Customer receipts in the period by day, bank account (method), currency and the user who posted them.",
            "RecvCollections", [new Column("Date", "Date", 75), new Column("Method", "Bank / method", 150), new Column("Currency", "Currency", 55), new Column("User", "Posted by", 90), new Column("Count", "Receipts", 55),
             new Column("Amount", "In currency", 95), new Column("Base", "Base", 95, true)], Period);
        yield return Listing("AR39", "Unallocated Receipts, Credits & Advances", "Sales", "Receipts, credit notes and advances not yet allocated to invoices at the date, with how long they have been unallocated.",
            "RecvUnallocated", [new Column("Customer", "Customer", 150), new Column("Document", "Document", 110), new Column("Type", "Type", 80), new Column("Date", "Date", 75), new Column("Days", "Days", 50),
             new Column("Unallocated", "Unallocated", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR40", "Debtor Days (DSO)", "Sales", "Per customer: what is owed at the date against sales over the 90 days to it, as days of sales outstanding.",
            "RecvDso", [new Column("Customer", "Customer", 160), new Column("Sales90", "Sales, 90 days", 100), new Column("Dso", "DSO (days)", 70), new Column("Terms", "Terms", 50), new Column("Owing", "Owing", 100, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR41", "Promise-to-Pay Diary", "Sales", "Promises recorded by credit control: amount, date promised and whether kept, broken or still open at the date.",
            "RecvPromises", [new Column("PayBy", "Pay by", 75), new Column("Customer", "Customer", 150), new Column("Made", "Made", 75), new Column("Status", "Status", 70), new Column("Resolved", "Resolved", 75),
             new Column("Note", "Note", 180), new Column("Amount", "Amount", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR43", "Bad Debt Write-off Listing", "Sales", "Write-offs in the period with the customer, document and the debt case that approved it.",
            "RecvWriteOffs", [new Column("Date", "Date", 75), new Column("Customer", "Customer", 150), new Column("Document", "Document", 110), new Column("Case", "Debt case", 170), new Column("Amount", "Amount", 90, true)],
            Period);
        yield return Listing("AR45", "Lay-bye Status & Arrears", "Sales", "Every lay-bye plan with price, paid, balance and days to (or past) its final date.",
            "RecvLaybys", [new Column("Plan", "Plan", 80), new Column("Customer", "Customer", 150), new Column("Opened", "Opened", 75), new Column("Final", "Final date", 75), new Column("Status", "Status", 70),
             new Column("Price", "Price", 90), new Column("Paid", "Paid", 90), new Column("Due", "Days", 70), new Column("Balance", "Balance", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR47", "Debtor Exposure by Currency", "Sales", "What customers owe at the date in each currency, in transaction and base, with each currency's share.",
            "RecvExposure", [new Column("Currency", "Currency", 60), new Column("Customers", "Customers", 65), new Column("Transaction", "Transaction", 110), new Column("Share", "Share of base", 80),
             new Column("Base", "Base", 110, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AR48", "New & Dormant Customers", "Sales", "Customers first invoiced in the period, and customers not invoiced for 90 days to its end.",
            "RecvNewDormant", [new Column("Status", "Status", 70), new Column("Customer", "Customer", 160), new Column("First", "First invoice", 80), new Column("Last", "Last invoice", 80),
             new Column("Lifetime", "Lifetime sales", 100, true)], Period);
    }
}
