using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// General ledger reports from brief §14 A not already in the core catalogue (codes are GL plus the brief's item number).
/// Each definition is a listing over a <c>Gled*</c> data command served by the ledger report data provider.
/// </summary>
public static class LedgerReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every GL-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("GL2", "Trial Balance — Multi-currency", "GeneralLedger", "Posted balances as at the date per account and transaction currency, in transaction and base.",
            "GledMultiCurrencyTb", [new Column("Account", "Account", 70), new Column("Name", "Name", 180), new Column("Currency", "Currency", 55), new Column("Transaction", "Transaction balance", 110),
             new Column("Base", "Base balance", 110, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL5", "Journal Listing & Journal Audit", "GeneralLedger", "Every journal in the period with status, source, manual flag, who prepared and approved it, reversals and value.",
            "GledJournals", [new Column("Journal", "Journal", 100), new Column("Date", "Date", 75), new Column("Status", "Status", 85), new Column("Source", "Source", 55), new Column("Document", "Document", 140),
             new Column("Manual", "Manual", 50), new Column("PreparedBy", "Prepared by", 85), new Column("ApprovedBy", "Approved by", 85), new Column("Reversal", "Reversal", 100),
             new Column("Value", "Value", 95, true)], Period);
        yield return Listing("GL8", "Statement of Changes in Equity", "GeneralLedger", "Each equity account's opening balance, the period's profit and other movements, and closing balance.",
            "GledEquity", [new Column("Line", "Line", 200), new Column("Opening", "Opening", 100), new Column("Profit", "Profit for the period", 110), new Column("Movements", "Other movements", 110),
             new Column("Closing", "Closing", 110, true)], Period);
        yield return Listing("GL12", "Budget vs Actual vs Commitment", "GeneralLedger", "Year to date per budgeted account and department: budget, actual, open commitments, what is still available and the share used.",
            "GledBudgetCommitment", [new Column("Account", "Account", 70), new Column("Name", "Name", 160), new Column("Department", "Department", 85), new Column("Budget", "Budget YTD", 95),
             new Column("Actual", "Actual YTD", 95), new Column("Committed", "Committed", 95), new Column("Used", "Used %", 60), new Column("Available", "Available", 100, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL14", "Consolidation Elimination Schedule", "GeneralLedger", "Every intercompany document across the group in the period: each side's amount, and whether it eliminates or is unmatched.",
            "GledEliminations", [new Column("Document", "Document", 110), new Column("Date", "Date", 75), new Column("FromCompany", "Company", 90), new Column("FromAmount", "Its side", 95), new Column("ToCompany", "Counterparty", 90),
             new Column("ToAmount", "Their side", 95), new Column("Status", "Status", 90), new Column("Eliminated", "Eliminated", 95, true)], Period);
        yield return Listing("GL15", "FX Revaluation Report", "GeneralLedger", "Unrealised revaluations of foreign-currency balances (ledger and trading) in the period, by account.",
            "GledFxRevaluation", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Type", "Type", 130), new Column("Narrative", "Narrative", 200), new Column("Account", "Account", 70),
             new Column("Amount", "Gain / (loss)", 95, true)], Period);
        yield return Listing("GL16", "Realised FX Gain / Loss", "GeneralLedger", "Exchange differences realised on settlement in the period.",
            "GledRealisedFx", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Document", "Document", 120), new Column("Narrative", "Narrative", 220), new Column("Account", "Account", 70),
             new Column("Amount", "Gain / (loss)", 95, true)], Period);
        yield return Listing("GL17", "Suspense & Unallocated Items Review", "GeneralLedger", "Balances on suspense accounts and unallocated receipts and payments on the trading ledgers at the date.",
            "GledSuspense", [new Column("Item", "Item", 180), new Column("Reference", "Account / ledger", 120), new Column("Count", "Items", 50), new Column("Oldest", "Oldest", 75), new Column("Balance", "Balance", 100, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL18", "Control Reconciliation — VAT and Bank", "GeneralLedger", "Tax accounts against the tax-coded postings, and each bank account's ledger balance against its statement, with what explains the gap.",
            "GledControlLegs", [new Column("Control", "Control", 110), new Column("Account", "Account", 90), new Column("Ledger", "Ledger", 100), new Column("Other", "Sub-ledger / statement", 120), new Column("Explained", "Explained by", 220),
             new Column("Difference", "Difference", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL20", "Year-End Pack", "GeneralLedger", "For the year containing the date: the close checklist, the trial balance totals, the result for the year and the year's journals by source.",
            "GledYearEnd", [new Column("Section", "Section", 110), new Column("Item", "Item", 260), new Column("Value", "Value", 160)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL22", "Recurring Journals Due & Missed", "GeneralLedger", "Each active recurring template's last posted month against the date: posted, due or missed.",
            "GledRecurringDue", [new Column("Code", "Code", 80), new Column("Description", "Description", 180), new Column("LastPosted", "Last posted", 75), new Column("Status", "Status", 120),
             new Column("Value", "Monthly value", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("GL23", "Accruals, Prepayments & Deferred Income Schedule", "GeneralLedger", "Accrual and prepayment journals in the period, then deferred income schedules with what is left to recognise.",
            "GledAccruals", [new Column("Kind", "Kind", 100), new Column("Reference", "Reference", 110), new Column("Date", "Date / start", 75), new Column("Detail", "Detail", 220), new Column("Recognised", "Recognised", 90),
             new Column("Amount", "Amount / remaining", 100, true)], Period);
        yield return Listing("GL24", "Intercompany Balances & Reconciliation", "GeneralLedger", "Intercompany journals in the period and whether the counterparty company holds the mirror entry.",
            "GledIntercompany", [new Column("Document", "Document", 110), new Column("Date", "Date", 75), new Column("Narrative", "Narrative", 200), new Column("Counterparty", "Counterparty", 110),
             new Column("Status", "Status", 80), new Column("Amount", "Amount", 95, true)], Period);
        yield return Listing("GL25", "Hyperinflation (IAS 29) Working Papers", "GeneralLedger", "Each account's historical balance restated by the price index at the date, monetary items unchanged, with the restatement.",
            "GledHyperinflation", [new Column("Account", "Account", 70), new Column("Name", "Name", 160), new Column("Type", "Type", 70), new Column("Monetary", "Monetary", 60), new Column("Historical", "Historical", 100),
             new Column("Restated", "Restated", 100), new Column("Restatement", "Restatement", 100, true)], [("AsAtDate", ParameterType.Date), ("IndexCode", ParameterType.String)]);
    }
}
