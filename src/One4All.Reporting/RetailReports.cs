using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// POS / retail operations reports from brief §14 F not already in the core catalogue (codes are POS plus the brief's item
/// number). Each definition is a listing over a <c>Retl*</c> data command served by the retail report data provider.
/// </summary>
public static class RetailReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every POS-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("POS103", "Daily Sales Summary", "PointOfSale", "Per day, till and cashier: sales, voids, net, tax, the tender split and gross takings.",
            "RetlDaily", [new Column("Date", "Date", 75), new Column("Till", "Till", 70), new Column("Cashier", "Cashier", 90), new Column("Sales", "Sales", 50), new Column("Voided", "Voided", 50),
             new Column("Net", "Net", 90), new Column("Tax", "Tax", 80), new Column("Tenders", "Tenders", 220), new Column("Gross", "Gross", 95, true)], Period);
        yield return Listing("POS104", "Cashier Performance", "PointOfSale", "Per cashier over the period: sessions, sales, items, average basket, voids, cash-up variance and net sales.",
            "RetlCashiers", [new Column("Cashier", "Cashier", 100), new Column("Sessions", "Sessions", 60), new Column("Sales", "Sales", 55), new Column("Items", "Items", 55), new Column("AverageBasket", "Avg basket", 80),
             new Column("Voids", "Voids", 50), new Column("Variance", "Cash variance", 90), new Column("Net", "Net sales", 95, true)], Period);
        yield return Listing("POS105", "Till Variance & Cash-up Report", "PointOfSale", "Each closed till session: float, cash taken, change given, expected, counted and the over or short.",
            "RetlCashUp", [new Column("Session", "Session", 60), new Column("Till", "Till", 70), new Column("Cashier", "Cashier", 90), new Column("Closed", "Closed", 115), new Column("Float", "Float", 75),
             new Column("CashIn", "Cash taken", 85), new Column("Change", "Change", 75), new Column("Expected", "Expected", 85), new Column("Counted", "Counted", 85), new Column("Variance", "Over / (short)", 90, true)], Period);
        yield return Listing("POS112", "Hourly Sales Heatmap & Basket Analysis", "PointOfSale", "Sales by weekday and hour: count, items per basket, average basket and net.",
            "RetlHeatmap", [new Column("Weekday", "Weekday", 80), new Column("Hour", "Hour", 45), new Column("Sales", "Sales", 50), new Column("ItemsPerBasket", "Items / basket", 75),
             new Column("AverageBasket", "Avg basket", 80), new Column("Net", "Net", 95, true)], Period);
        yield return Listing("POS113", "Product Mix Ranking", "PointOfSale", "Items ranked by POS net sales in the period, with quantity, share and cumulative share.",
            "RetlProductMix", [new Column("Rank", "Rank", 40), new Column("Item", "Item", 170), new Column("Quantity", "Qty", 60), new Column("Share", "Share", 60), new Column("Cumulative", "Cumulative", 70),
             new Column("Net", "Net sales", 95, true)], Period);
        yield return Listing("POS114", "Promotion Effectiveness", "PointOfSale", "Each promotion's redemptions in the period, the spend they drove and the discount given.",
            "RetlPromotions", [new Column("Campaign", "Campaign", 90), new Column("Name", "Name", 150), new Column("Valid", "Valid", 150), new Column("Redemptions", "Redemptions", 75), new Column("Spend", "Spend", 95),
             new Column("AverageSpend", "Avg spend", 80), new Column("DiscountRate", "Discount / spend", 85), new Column("Discount", "Discount", 90, true)], Period);
        yield return Listing("POS117", "Tender & Multi-currency Tender Analysis", "PointOfSale", "Tenders taken in the period by type and currency, in currency and base, net of change.",
            "RetlTenders", [new Column("Tender", "Tender", 90), new Column("Currency", "Currency", 55), new Column("Count", "Count", 50), new Column("Amount", "In currency", 95), new Column("Share", "Share", 60),
             new Column("Base", "Base", 95, true)], Period);
        yield return Listing("POS120", "Offline Sync Log", "PointOfSale", "Operations captured offline in the period: kind, status, attempts, when synced and any error.",
            "RetlOffline", [new Column("Created", "Created", 115), new Column("Kind", "Kind", 110), new Column("Status", "Status", 70), new Column("Attempts", "Attempts", 55), new Column("Synced", "Synced", 115),
             new Column("Error", "Last error", 240)], Period);
    }
}
