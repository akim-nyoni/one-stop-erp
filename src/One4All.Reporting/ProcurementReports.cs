using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Procurement reports from brief §14 G not already in the core catalogue (codes are PO plus the brief's item number).
/// Each definition is a listing over a <c>Proc*</c> data command served by the procurement report data provider.
/// </summary>
public static class ProcurementReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every PO-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("PO124", "Requisition Approval Turnaround", "Purchasing", "Requisitions in the period with who approved them and how many days it took.",
            "ProcRequisitionTurnaround", [new Column("Requisition", "Requisition", 90), new Column("Supplier", "Supplier", 140), new Column("Requested", "Requested", 75), new Column("Status", "Status", 75),
             new Column("Approver", "Approved by", 90), new Column("Approved", "Approved", 75), new Column("Days", "Days", 45), new Column("Value", "Value", 95, true)], Period);
        yield return Listing("PO125", "Approval History / Authorisation Audit", "Purchasing", "Every workflow action in the period: document, stage, who, what and when, with the document value.",
            "ProcApprovalHistory", [new Column("When", "When", 115), new Column("Document", "Document", 110), new Column("Reference", "Reference", 100), new Column("Stage", "Stage", 45),
             new Column("User", "User", 90), new Column("Action", "Action", 65), new Column("Comment", "Comment", 180), new Column("Value", "Value", 95, true)], Period);
        yield return Listing("PO126", "Purchase Order Listing", "Purchasing", "Every order in the period with status, required-by date, overdue flag and the value still to be received.",
            "ProcOrders", [new Column("Order", "Order", 85), new Column("Date", "Date", 75), new Column("Supplier", "Supplier", 140), new Column("Status", "Status", 85), new Column("RequiredBy", "Required by", 75),
             new Column("Overdue", "Overdue", 70), new Column("Currency", "Currency", 55), new Column("Ordered", "Ordered", 90), new Column("Received", "Received", 90),
             new Column("Outstanding", "To receive", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("AsAtDate", ParameterType.Date)]);
        yield return Listing("PO128", "Committed Cost by Budget Line", "Purchasing", "Commitments raised against the budget, by account and department, with their status.",
            "ProcCommitments", [new Column("Account", "Account", 80), new Column("Department", "Department", 90), new Column("Period", "Period", 50), new Column("Reference", "Reference", 110),
             new Column("Type", "Budget", 70), new Column("Status", "Status", 85), new Column("Amount", "Amount", 95, true)], []);
        yield return Listing("PO129", "Goods Received Listing & Variance", "Purchasing", "Goods received in the period against what was ordered, with quantity and price variances.",
            "ProcGoodsReceived", [new Column("Grn", "GRN", 85), new Column("Date", "Date", 75), new Column("Order", "Order", 85), new Column("Supplier", "Supplier", 130), new Column("Item", "Item", 120),
             new Column("Ordered", "Ordered", 60), new Column("Received", "Received to date", 75), new Column("QuantityVariance", "Qty variance", 70), new Column("PriceVariance", "Price variance", 80),
             new Column("Value", "Value", 90, true)], Period);
        yield return Listing("PO130", "Rejected Goods / Returns", "Purchasing", "Quality inspections in the period that rejected goods, with the reason and the value rejected.",
            "ProcRejections", [new Column("Grn", "GRN", 85), new Column("Inspected", "Inspected", 75), new Column("Supplier", "Supplier", 140), new Column("Item", "Item", 130), new Column("Passed", "Passed", 60),
             new Column("Rejected", "Rejected", 60), new Column("Reason", "Reason", 170), new Column("Value", "Value rejected", 95, true)], Period);
        yield return Listing("PO131", "RFQ Comparison & Award", "Purchasing", "Every quote received on each RFQ, ranked by total price, with lead time and the award and its reason.",
            "ProcRfqs", [new Column("Rfq", "RFQ", 85), new Column("Supplier", "Supplier", 140), new Column("Received", "Received", 75), new Column("LeadDays", "Lead days", 60), new Column("Rank", "Rank", 45),
             new Column("Awarded", "Awarded", 60), new Column("Reason", "Award reason", 180), new Column("Total", "Quoted total", 95, true)], []);
        yield return Listing("PO132", "Purchase Price Variance", "Purchasing", "Each item ordered in the period at a different price from the order before it, with the variance on the quantity ordered.",
            "ProcPriceVariance", [new Column("Item", "Item", 140), new Column("Order", "Order", 85), new Column("Date", "Date", 75), new Column("Supplier", "Supplier", 130), new Column("Previous", "Previous price", 85),
             new Column("Price", "Price", 80), new Column("Change", "Change", 60), new Column("Quantity", "Qty", 55), new Column("Variance", "Variance", 90, true)], Period);
        yield return Listing("PO134", "Procurement Spend Analysis", "Purchasing", "Order value in the period by supplier and currency, with lines, items and share.",
            "ProcSpend", [new Column("Supplier", "Supplier", 150), new Column("Currency", "Currency", 55), new Column("Orders", "Orders", 50), new Column("Lines", "Lines", 50), new Column("Items", "Items", 50),
             new Column("Share", "Share", 60), new Column("Spend", "Spend", 100, true)], Period);
        yield return Listing("PO135", "Supplier Lead Time Analysis", "Purchasing", "Per supplier: days from order to first receipt (average, fastest, slowest) and orders received by their required date.",
            "ProcLeadTimes", [new Column("Supplier", "Supplier", 150), new Column("Orders", "Orders received", 70), new Column("Average", "Average days", 70), new Column("Fastest", "Fastest", 55),
             new Column("Slowest", "Slowest", 55), new Column("OnTime", "On time", 70)], Period);
    }
}
