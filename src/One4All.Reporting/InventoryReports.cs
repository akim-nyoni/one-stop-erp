using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Inventory reports from brief §14 E not already in the core catalogue (codes are INV plus the brief's item number).
/// Each definition is a listing over a <c>Stk*</c> data command served by the inventory report data provider.
/// </summary>
public static class InventoryReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every INV-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("INV78", "Stock Valuation vs GL Stock Control", "Inventory", "The stock register's value at the date by warehouse against the stock control accounts, with the difference.",
            "StkValuationVsGl", [new Column("Line", "Line", 200), new Column("Items", "Items", 55), new Column("Quantity", "Quantity", 80), new Column("Value", "Value", 110, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("INV79", "Stock on Hand & Availability", "Inventory", "Per item and warehouse: on hand, committed to orders, on order, free and available to promise.",
            "StkAvailability", [new Column("Item", "Item", 150), new Column("Warehouse", "Warehouse", 90), new Column("OnHand", "On hand", 70), new Column("Committed", "Committed", 70), new Column("OnOrder", "On order", 70),
             new Column("Free", "Free", 70), new Column("Atp", "ATP", 70), new Column("Value", "Value", 95, true)], []);
        yield return Listing("INV81", "Stocktake Count Sheets & Variance", "Inventory", "Each stocktake's lines: system quantity, counted, variance in quantity and value (uncounted lines flagged).",
            "StkStocktake", [new Column("Stocktake", "Stocktake", 90), new Column("Date", "Date", 75), new Column("Warehouse", "Warehouse", 90), new Column("Status", "Status", 60), new Column("Item", "Item", 150),
             new Column("System", "System", 65), new Column("Counted", "Counted", 65), new Column("Variance", "Variance", 65), new Column("Value", "Variance value", 95, true)], []);
        yield return Listing("INV82", "Stock Adjustments by Reason Code", "Inventory", "Adjustments in the period per reason code: count, units gained and lost, and the net value.",
            "StkReasons", [new Column("Reason", "Reason", 80), new Column("Name", "Description", 180), new Column("Count", "Adjustments", 75), new Column("Gained", "Units gained", 85), new Column("Lost", "Units lost", 85),
             new Column("Value", "Net value", 95, true)], Period);
        yield return Listing("INV86", "Overstock Report", "Inventory", "Items holding more than their reorder level plus reorder quantity (or more than 90 days of issues), with the excess value.",
            "StkOverstock", [new Column("Item", "Item", 150), new Column("OnHand", "On hand", 70), new Column("Ceiling", "Ceiling", 70), new Column("Excess", "Excess", 70), new Column("DaysCover", "Days cover", 70),
             new Column("Value", "Excess value", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("INV87", "Negative Stock Report", "Inventory", "Item and warehouse balances below zero — an integrity exception to clear.",
            "StkNegative", [new Column("Item", "Item", 150), new Column("Warehouse", "Warehouse", 90), new Column("OnHand", "On hand", 70), new Column("AverageCost", "Average cost", 85), new Column("Value", "Value", 95, true)], []);
        yield return Listing("INV88", "Batch / Lot Stock Report", "Inventory", "Batch-tracked stock on hand at the date per item, warehouse and batch, with first receipt, expiry and value at average cost.",
            "StkBatches", [new Column("Item", "Item", 150), new Column("Warehouse", "Warehouse", 80), new Column("Batch", "Batch", 100), new Column("Received", "First received", 80), new Column("Expiry", "Expiry", 75),
             new Column("OnHand", "On hand", 70), new Column("Value", "Value", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("INV89", "Serial Number Register", "Inventory", "Every serial number: where it is, when it came in and when and how it last moved, and whether it is still in stock.",
            "StkSerials", [new Column("Item", "Item", 150), new Column("Serial", "Serial", 110), new Column("Status", "Status", 70), new Column("Warehouse", "Warehouse", 80), new Column("Received", "Received", 75),
             new Column("LastMoved", "Last moved", 75), new Column("LastMovement", "Last movement", 160)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("INV90", "Expiry Report", "Inventory", "Batches on hand that have expired or expire within the window, soonest first, with the value at risk.",
            "StkExpiry", [new Column("Item", "Item", 150), new Column("Warehouse", "Warehouse", 80), new Column("Batch", "Batch", 100), new Column("Expiry", "Expiry", 75), new Column("Days", "Days left", 60),
             new Column("Status", "Status", 70), new Column("OnHand", "On hand", 70), new Column("Value", "Value at risk", 95, true)], [("AsAtDate", ParameterType.Date), ("Days", ParameterType.Integer)]);
        yield return Listing("INV92", "Stock Turnover & Days Cover", "Inventory", "Per item over the period: cost of issues, average stock value, turnover and days of cover at the current rate.",
            "StkTurnover", [new Column("Item", "Item", 150), new Column("Issued", "Cost issued", 95), new Column("AverageStock", "Average stock", 95), new Column("Turns", "Turns", 55), new Column("DaysCover", "Days cover", 70),
             new Column("Value", "Stock value", 95, true)], Period);
        yield return Listing("INV93", "Gross Margin by Item", "Inventory", "Per item over the period: net sales (POS and invoiced orders), cost of issues and the margin.",
            "StkMargin", [new Column("Item", "Item", 150), new Column("Category", "Category", 90), new Column("Quantity", "Qty sold", 65), new Column("Sales", "Net sales", 95), new Column("Cost", "Cost", 95),
             new Column("MarginPercent", "Margin %", 60), new Column("Margin", "Margin", 95, true)], Period);
        yield return Listing("INV96", "Cost Change History", "Inventory", "Each receipt at a unit cost different from the item's previous receipt, with the change.",
            "StkCostChanges", [new Column("Item", "Item", 150), new Column("Date", "Date", 75), new Column("Reference", "Reference", 110), new Column("Previous", "Previous cost", 85), new Column("Cost", "New cost", 85),
             new Column("Change", "Change", 60), new Column("Quantity", "Qty", 60)], Period);
        yield return Listing("INV97", "Landed Cost Analysis", "Inventory", "Landed costs added to stock in the period, per item and reference, against the goods' own receipt value.",
            "StkLandedCost", [new Column("Date", "Date", 75), new Column("Reference", "Reference", 110), new Column("Item", "Item", 150), new Column("Goods", "Goods value", 95), new Column("Uplift", "Uplift %", 60),
             new Column("Landed", "Landed cost", 95, true)], Period);
    }
}
