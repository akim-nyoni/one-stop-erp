using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Executive / BI dashboards from brief §14 Q, printable as listings (codes are DB plus the brief's item number), over
/// <c>Dash*</c> data commands served by the dashboard report data provider.
/// </summary>
public static class DashboardReports
{
    /// <summary>Every DB-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("DB251", "Executive Dashboard", "System", "Year to date at the date: revenue, expenses, profit, cash, debtors, creditors, stock, working capital and the cash cycle.",
            "DashExecutive", [new Column("Measure", "Measure", 260), new Column("Value", "Value", 160)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("DB252", "Daily Flash Report", "System", "The day's sales, receipts and payments against the same day last week and the month to date, with cash at close.",
            "DashFlash", [new Column("Measure", "Measure", 180), new Column("Today", "Today", 110), new Column("LastWeek", "Same day last week", 120), new Column("MonthToDate", "Month to date", 120)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("DB253", "KPI Scorecard", "System", "This month against last: revenue, profit, gross margin, debtor, creditor and stock days, cash cycle — with the direction of travel.",
            "DashKpi", [new Column("Kpi", "KPI", 200), new Column("ThisMonth", "This month", 110), new Column("LastMonth", "Last month", 110), new Column("Trend", "Trend", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("DB254", "Branch Comparison League Table", "System", "Branches ranked by POS net sales in the period, with transactions, average basket and share.",
            "DashBranches", [new Column("Rank", "Rank", 40), new Column("Branch", "Branch", 150), new Column("Transactions", "Transactions", 80), new Column("AverageBasket", "Avg basket", 85),
             new Column("Share", "Share", 60), new Column("Sales", "Net sales", 100, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("DB255", "Trend & Seasonality Analysis", "System", "Revenue, expenses and profit month by month for the 24 months to the date, with each month against the same month a year before.",
            "DashTrend", [new Column("Month", "Month", 70), new Column("Revenue", "Revenue", 100), new Column("Expenses", "Expenses", 100), new Column("LastYear", "Revenue a year before", 120),
             new Column("YearOnYear", "YoY", 60), new Column("Profit", "Profit", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("DB256", "Municipal Executive Dashboard", "Municipal", "Billed and collected this year, collection rate, debtors and over-90s, indigent households, disconnections, open faults and licences due.",
            "DashMunicipal", [new Column("Measure", "Measure", 260), new Column("Value", "Value", 160)], [("AsAtDate", ParameterType.Date)]);
    }
}
