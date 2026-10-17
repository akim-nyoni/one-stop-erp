using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Accounts Payable reports from brief §14 C not already in the core catalogue (codes are AP plus the brief's item
/// number). Each definition is a listing over a <c>Payb*</c> data command served by the payables report data provider.
/// </summary>
public static class PayablesReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every AP-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("AP53", "Supplier Ledger", "Purchasing", "One supplier's documents in date order with running balance and what is still outstanding on each.",
            "PaybLedger", [new Column("Date", "Date", 75), new Column("Type", "Type", 80), new Column("Document", "Document", 110), new Column("Due", "Due", 75), new Column("Debit", "Debit", 90),
             new Column("Credit", "Credit", 90), new Column("Balance", "Balance owed", 95), new Column("Outstanding", "Outstanding", 95, true)], [("SupplierCode", ParameterType.String)]);
        yield return Listing("AP54", "Purchase Day Book", "Purchasing", "Supplier invoices, credit notes and debit notes posted in the period, in transaction and base currency.",
            "PaybDayBook", [new Column("Date", "Date", 75), new Column("Document", "Document", 110), new Column("Type", "Type", 80), new Column("Supplier", "Supplier", 150), new Column("Currency", "Currency", 55),
             new Column("Transaction", "Transaction", 95), new Column("Base", "Base", 95, true)], Period);
        yield return Listing("AP55", "Purchase Analysis by Supplier and Item", "Purchasing", "Order lines placed in the period (cancelled orders excluded) by supplier and item: quantity, average price and value.",
            "PaybPurchaseAnalysis", [new Column("Supplier", "Supplier", 150), new Column("Item", "Item", 150), new Column("Orders", "Orders", 50), new Column("Quantity", "Qty", 65), new Column("AveragePrice", "Avg price", 85),
             new Column("Currency", "Currency", 55), new Column("Value", "Value", 100, true)], Period);
        yield return Listing("AP57", "Payment Run Proposal", "Purchasing", "What a payment run on the date would pay: per supplier and currency the invoices due by the cut-off, the amount "
            + "due, credits applied, tax withheld, suppliers on hold or with a bank-detail change pending, and what is deferred when the funds given do not cover everything.",
            "PaybRunProposal", [new Column("Supplier", "Supplier", 80), new Column("Name", "Name", 150), new Column("Currency", "Ccy", 45), new Column("Priority", "Priority", 50),
             new Column("Invoices", "Invoices", 55), new Column("Due", "Due", 90), new Column("Credits", "Credits applied", 90), new Column("Withholding", "Withheld", 80),
             new Column("Status", "Status", 110), new Column("Deferred", "Deferred", 85), new Column("ToPay", "To pay", 95, true)],
            [("PaymentDate", ParameterType.Date), ("DueOnOrBefore", ParameterType.Date), ("Currency", ParameterType.String), ("FundsAvailable", ParameterType.Decimal)]);
        yield return Listing("AP58", "Payments Made", "Purchasing", "Payments to suppliers in the period (payment runs and single payments) with tax withheld.",
            "PaybPayments", [new Column("Date", "Date", 75), new Column("Document", "Document", 120), new Column("Supplier", "Supplier", 150), new Column("Currency", "Currency", 55),
             new Column("Transaction", "Transaction", 95), new Column("Withheld", "Tax withheld", 85), new Column("Base", "Base", 95, true)], Period);
        yield return Listing("AP59", "Goods Received Not Invoiced (GRNI)", "Purchasing", "Order lines received but not yet invoiced: the GRNI accrual by order and item, with its age.",
            "PaybGrni", [new Column("Order", "Order", 85), new Column("Supplier", "Supplier", 140), new Column("Item", "Item", 130), new Column("Received", "Received", 60), new Column("Invoiced", "Invoiced", 60),
             new Column("Price", "Price", 75), new Column("LastReceipt", "Last receipt", 80), new Column("Days", "Days", 45), new Column("Value", "Accrual", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AP60", "Invoice Matching Exceptions", "Purchasing", "Supplier invoices disputed against their order: reason, status and resolution.",
            "PaybDisputes", [new Column("Invoice", "Invoice", 100), new Column("Date", "Date", 75), new Column("Supplier", "Supplier", 140), new Column("Order", "Order", 85), new Column("Reason", "Reason", 180),
             new Column("Status", "Status", 70), new Column("Resolution", "Resolution", 160), new Column("Value", "Disputed value", 95, true)], []);
        yield return Listing("AP61", "Supplier Statement Reconciliation", "Purchasing", "Each supplier's latest captured statement at the date against our ledger: both balances, items only they show, items only we show, and amounts that differ.",
            "PaybStatementRecon", [new Column("Supplier", "Supplier", 140), new Column("Section", "Section", 160), new Column("Document", "Document", 110), new Column("Theirs", "Their amount", 95), new Column("Ours", "Our amount", 95),
             new Column("Difference", "Difference", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AP62", "Withholding Tax Deducted", "Purchasing", "Tax withheld from supplier payments in the period, by certificate, and whether it has been remitted.",
            "PaybWithholding", [new Column("Certificate", "Certificate", 90), new Column("Supplier", "Supplier", 140), new Column("Payment", "Payment", 110), new Column("Date", "Date", 75),
             new Column("Tax", "Tax", 60), new Column("Rate", "Rate %", 50), new Column("Gross", "Gross", 90), new Column("Remitted", "Remitted", 75), new Column("Withheld", "Withheld", 90, true)], Period);
        yield return Listing("AP63", "Supplier Performance Scorecard", "Purchasing", "Per supplier in the period: deliveries, on time, quantity received and returned, quality and spend.",
            "PaybScorecard", [new Column("Supplier", "Supplier", 150), new Column("Deliveries", "Deliveries", 65), new Column("OnTime", "On time", 70), new Column("Received", "Received", 70),
             new Column("Returned", "Returned", 65), new Column("Quality", "Quality", 65), new Column("Spend", "Spend", 95, true)], Period);
        yield return Listing("AP64", "Supplier Balances (with debit balances)", "Purchasing", "What is owed to each supplier at the date; suppliers in debit (owing us) flagged, with hold and blacklist status.",
            "PaybBalances", [new Column("Supplier", "Supplier", 150), new Column("Currency", "Currency", 55), new Column("Flag", "Flag", 90), new Column("Status", "Status", 110), new Column("Balance", "Balance owed", 100, true),
             new Column("Advances", "Advances held", 100, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("AP65", "Supplier Bank Detail Change Audit", "Purchasing", "Every request to change a supplier's bank details: who asked, who reviewed, outcome (account numbers masked).",
            "PaybBankChanges", [new Column("Requested", "Requested", 110), new Column("Supplier", "Supplier", 140), new Column("RequestedBy", "Requested by", 90), new Column("NewDetails", "New details", 200),
             new Column("Status", "Status", 70), new Column("ReviewedBy", "Reviewed by", 90), new Column("Reason", "Rejection reason", 160)], []);
        yield return Listing("AP66", "Top Suppliers & Concentration", "Purchasing", "Suppliers ranked by net purchases in the period, with share and cumulative share.",
            "PaybTopSuppliers", [new Column("Rank", "Rank", 40), new Column("Supplier", "Supplier", 160), new Column("Documents", "Documents", 65), new Column("Share", "Share", 60),
             new Column("Cumulative", "Cumulative", 70), new Column("Purchases", "Net purchases", 100, true)], Period);
        yield return Listing("AP67", "Supplier Advances & Prepayments", "Purchasing", "Every advance paid to a supplier in the period (or still held at its end): the purchase order it is for, what was applied to "
            + "invoices, what the supplier refunded and the balance still held in the advances account.",
            "PaybAdvances", [new Column("Supplier", "Supplier", 90), new Column("Document", "Document", 100), new Column("Date", "Paid", 75), new Column("Order", "Purchase order", 100), new Column("Currency", "Currency", 55),
             new Column("Amount", "Paid", 90), new Column("Applied", "Applied", 90), new Column("Refunded", "Refunded", 90), new Column("Outstanding", "Held", 90, true), new Column("Status", "Status", 70)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
    }
}
