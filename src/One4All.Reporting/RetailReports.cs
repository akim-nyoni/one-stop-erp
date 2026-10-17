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
        yield return Listing("POS102", "POS Z-Read / X-Read by Till & Cashier", "PointOfSale", "Every till session in the period: till, cashier, opened and closed, Z-read number (open sessions are an "
            + "X-read so far), float, sales, and the takings per tender and currency.", "RetlZRead",
            [new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 90), new Column("Opened", "Opened", 95), new Column("Closed", "Closed", 95), new Column("Read", "Read", 55),
             new Column("Float", "Float", 70), new Column("Sales", "Sales", 45), new Column("Tender", "Tender", 80), new Column("Currency", "Currency", 50), new Column("Amount", "Amount", 85),
             new Column("Base", "Base", 90, true)], Period);
        yield return Listing("POS103", "Daily Sales Summary", "PointOfSale", "Per day, till and cashier: sales, voids, net, tax, the tender split and gross takings.",
            "RetlDaily", [new Column("Date", "Date", 75), new Column("Till", "Till", 70), new Column("Cashier", "Cashier", 90), new Column("Sales", "Sales", 50), new Column("Voided", "Voided", 50),
             new Column("Net", "Net", 90), new Column("Tax", "Tax", 80), new Column("Tenders", "Tenders", 220), new Column("Gross", "Gross", 95, true)], Period);
        yield return Listing("POS104", "Cashier Performance", "PointOfSale", "Per cashier over the period: sessions, sales, items, average basket, voids, cash-up variance and net sales.",
            "RetlCashiers", [new Column("Cashier", "Cashier", 100), new Column("Sessions", "Sessions", 60), new Column("Sales", "Sales", 55), new Column("Items", "Items", 55), new Column("AverageBasket", "Avg basket", 80),
             new Column("Voids", "Voids", 50), new Column("Variance", "Cash variance", 90), new Column("Net", "Net sales", 95, true)], Period);
        yield return Listing("POS105", "Till Variance & Cash-up Report", "PointOfSale", "Each closed till session: float, cash taken, change given, expected, counted and the over or short.",
            "RetlCashUp", [new Column("Session", "Session", 60), new Column("Till", "Till", 70), new Column("Cashier", "Cashier", 90), new Column("Closed", "Closed", 115), new Column("Float", "Float", 75),
             new Column("CashIn", "Cash taken", 85), new Column("Change", "Change", 75), new Column("Expected", "Expected", 85), new Column("Counted", "Counted", 85), new Column("Variance", "Over / (short)", 90, true)], Period);
        yield return Listing("POS106", "Void & Cancellation Analysis by Reason", "PointOfSale", "Lines and sales voided in the period with who, why and who authorised it.",
            "RetlVoids", [new Column("Date", "Date", 75), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Kind", "Kind", 70), new Column("Sale", "Sale", 90),
             new Column("Description", "Item / sale", 150), new Column("Reason", "Reason", 150), new Column("AuthorisedBy", "Authorised by", 85), new Column("Amount", "Value", 90, true)], Period);
        yield return Listing("POS107", "Refunds & Returns Register", "PointOfSale", "Every refund in the period: the sale and line, quantity, reason, authoriser, journal and whether the goods were restocked.",
            "RetlRefunds", [new Column("Date", "Date", 75), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Sale", "Sale", 90), new Column("Description", "Item", 150),
             new Column("Quantity", "Qty", 45), new Column("Reason", "Reason", 140), new Column("AuthorisedBy", "Authorised by", 85), new Column("Journal", "Journal", 95), new Column("Restock", "Stock", 85),
             new Column("Amount", "Refunded", 90, true)], Period);
        yield return Listing("POS108", "Line Discounts Given", "PointOfSale", "Discounts keyed on lines in the period, with the list amount, the rate and the reason, by cashier.",
            "RetlDiscounts", [new Column("Date", "Date", 75), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Sale", "Sale", 90), new Column("Description", "Item", 150),
             new Column("List", "List amount", 85), new Column("Rate", "Rate", 55), new Column("Reason", "Reason", 140), new Column("Amount", "Discount", 90, true)], Period);
        yield return Listing("POS109", "Price Overrides", "PointOfSale", "Prices keyed over list in the period: list and keyed price, the difference, reason and the authorising supervisor.",
            "RetlOverrides", [new Column("Date", "Date", 75), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("AuthorisedBy", "Authorised by", 85), new Column("Sale", "Sale", 90),
             new Column("Description", "Item", 150), new Column("List", "List price", 80), new Column("Keyed", "Keyed price", 80), new Column("Reason", "Reason", 140), new Column("Difference", "Difference", 90, true)], Period);
        yield return Listing("POS110", "No-Sale Drawer Openings", "PointOfSale", "Every drawer opened without a sale in the period, with the reason and authoriser, and each cashier's count.",
            "RetlNoSale", [new Column("Opened", "Opened", 115), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Reason", "Reason", 200), new Column("AuthorisedBy", "Authorised by", 85),
             new Column("CashierCount", "Cashier's no-sales", 90)], Period);
        yield return Listing("POS111", "Parked / Suspended Sales", "PointOfSale", "Sales parked in the period and whether they were resumed or abandoned, with the time they sat.",
            "RetlParked", [new Column("Parked", "Parked", 115), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Sale", "Sale", 90), new Column("Status", "Status", 80),
             new Column("Resumed", "Resumed", 115), new Column("Waited", "Waited", 70), new Column("Amount", "Value", 90, true)], Period);
        yield return Listing("POS112", "Hourly Sales Heatmap & Basket Analysis", "PointOfSale", "Sales by weekday and hour: count, items per basket, average basket and net.",
            "RetlHeatmap", [new Column("Weekday", "Weekday", 80), new Column("Hour", "Hour", 45), new Column("Sales", "Sales", 50), new Column("ItemsPerBasket", "Items / basket", 75),
             new Column("AverageBasket", "Avg basket", 80), new Column("Net", "Net", 95, true)], Period);
        yield return Listing("POS113", "Product Mix Ranking", "PointOfSale", "Items ranked by POS net sales in the period, with quantity, share and cumulative share.",
            "RetlProductMix", [new Column("Rank", "Rank", 40), new Column("Item", "Item", 170), new Column("Quantity", "Qty", 60), new Column("Share", "Share", 60), new Column("Cumulative", "Cumulative", 70),
             new Column("Net", "Net sales", 95, true)], Period);
        yield return Listing("POS114", "Promotion Effectiveness", "PointOfSale", "Each promotion's redemptions in the period, the spend they drove and the discount given.",
            "RetlPromotions", [new Column("Campaign", "Campaign", 90), new Column("Name", "Name", 150), new Column("Valid", "Valid", 150), new Column("Redemptions", "Redemptions", 75), new Column("Spend", "Spend", 95),
             new Column("AverageSpend", "Avg spend", 80), new Column("DiscountRate", "Discount / spend", 85), new Column("Discount", "Discount", 90, true)], Period);
        yield return Listing("POS115", "Loyalty Points Statement", "PointOfSale", "Per loyalty customer: points earned and redeemed in the period and the balance to date.",
            "RetlLoyalty", [new Column("Customer", "Customer", 120), new Column("Transactions", "Transactions", 75), new Column("Earned", "Earned", 85), new Column("Redeemed", "Redeemed", 85),
             new Column("LastActivity", "Last activity", 80), new Column("Balance", "Balance to date", 95, true)], Period);
        yield return Listing("POS116", "Receipt Reprint Log", "PointOfSale", "Every receipt reprinted in the period: when, by whom, the sale and its value.",
            "RetlReprints", [new Column("Printed", "Printed", 115), new Column("Till", "Till", 60), new Column("Cashier", "Cashier", 85), new Column("Sale", "Sale", 90), new Column("Reason", "Reason", 180),
             new Column("Amount", "Receipt value", 90, true)], Period);
        yield return Listing("POS117", "Tender & Multi-currency Tender Analysis", "PointOfSale", "Tenders taken in the period by type and currency, in currency and base, net of change.",
            "RetlTenders", [new Column("Tender", "Tender", 90), new Column("Currency", "Currency", 55), new Column("Count", "Count", 50), new Column("Amount", "In currency", 95), new Column("Share", "Share", 60),
             new Column("Base", "Base", 95, true)], Period);
        yield return Listing("POS118", "Table / Tab Report", "PointOfSale", "Tabs opened in the period: those still open with their table, waiter, party and spend so far, then per waiter the "
            + "tabs, average minutes from opening to the bill, average spend (before tips) and tips.", "RetlTabs",
            [new Column("Section", "Section", 70), new Column("Waiter", "Waiter", 90), new Column("Tab", "Tab / table", 110), new Column("Opened", "Opened", 95), new Column("Tabs", "Tabs", 45),
             new Column("Minutes", "Avg minutes", 60), new Column("Spend", "Spend", 90), new Column("Tips", "Tips", 80, true)], Period);
        yield return Listing("POS119", "Kitchen / Bar Ticket Volumes & Void-after-Print", "PointOfSale", "Per day and course: tickets sent to the kitchen or bar, items on them, how many reached "
            + "the table, and items voided after they were sent, with the value voided and the reasons.", "RetlKitchen",
            [new Column("Date", "Date", 75), new Column("Course", "Course", 80), new Column("Tickets", "Tickets", 50), new Column("Items", "Items", 50), new Column("Served", "Served", 50),
             new Column("Voided", "Voided after print", 70), new Column("Reasons", "Void reasons", 200), new Column("VoidValue", "Value voided", 90, true)], Period);
        yield return Listing("POS122", "Receipt / Invoice / Refund Slip Layouts", "PointOfSale", "A sale's slip as printed on 58 mm or 80 mm roll paper or an A4 page, with any refunds "
            + "taken against it as a refund slip below.", "RetlSlip", [new Column("Line", "#", 30), new Column("Text", "Slip", 520, true)],
            [("SaleNumber", ParameterType.String), ("Paper", ParameterType.String)]);
        yield return Listing("TX223", "Tax Invoice / Fiscal Receipt Compliance Layout", "Tax", "A sale laid out as a tax invoice - supplier and tax number, document number and time, lines with "
            + "tax, the tax summary by code, totals, the fiscal device status - followed by the compliance checks it passes or fails.", "RetlTaxInvoice",
            [new Column("Section", "Section", 90), new Column("Item", "Item", 220), new Column("Value", "Value", 220), new Column("Check", "Check", 60, true)], [("SaleNumber", ParameterType.String)]);
        yield return Listing("POS120", "Offline Sync Log", "PointOfSale", "Operations captured offline in the period: kind, status, attempts, when synced and any error.",
            "RetlOffline", [new Column("Created", "Created", 115), new Column("Kind", "Kind", 110), new Column("Status", "Status", 70), new Column("Attempts", "Attempts", 55), new Column("Synced", "Synced", 115),
             new Column("Error", "Last error", 240)], Period);
        yield return Listing("POS121", "Fiscal Submission Log", "PointOfSale", "Sales submitted to the fiscal device or revenue authority in the period, with the fiscal receipt reference and outcome.",
            "RetlFiscal", [new Column("Submitted", "Submitted", 115), new Column("Till", "Till", 60), new Column("Sale", "Sale", 90), new Column("Reference", "Fiscal reference", 120), new Column("Outcome", "Outcome", 200),
             new Column("Amount", "Value", 90, true)], Period);
        yield return Listing("POS124", "Gift Card Liability by Age", "PointOfSale", "Every gift card still carrying value as at the To date, aged from issue (0–90, 91–180, 181–365, over 365 days) with its "
            + "expiry and status: the liability on the books, and where breakage is likely.",
            "RetlGiftCards", [new Column("Card", "Card", 110), new Column("Currency", "Ccy", 45), new Column("Issued", "Issued", 75), new Column("Expires", "Expires", 75), new Column("Status", "Status", 65),
             new Column("AgeDays", "Days", 50), new Column("Band", "Band", 70), new Column("Original", "Original", 80), new Column("Balance", "Balance", 90, true)], Period);
        yield return Listing("POS123", "Allowance Vouchers Outstanding", "PointOfSale", "Fuel, meal and other allowance vouchers issued and not yet redeemed or cancelled as at the To date, per holder and kind, "
            + "with how many of them have expired.",
            "RetlAllowanceVouchers", [new Column("IssuedTo", "Issued to", 140), new Column("Kind", "Kind", 70), new Column("Count", "Count", 55), new Column("Expired", "Expired", 60), new Column("Serials", "Serials", 150),
             new Column("Value", "Value", 90, true)], Period);
    }
}
