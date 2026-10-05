using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Schools (brief §14 J, codes SC + item number), construction / job costing (§14 K, JC + item number) and sales order
/// processing (§14 H, SO + item number) reports not already in the core catalogue, over <c>Ops*</c> data commands served
/// by the operations report data provider.
/// </summary>
public static class OperationsReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every SC-, JC- and SO-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        // Schools.
        yield return Listing("SC175", "Fee Billing Run Summary", "School", "Fees billed in the period by term and grade: students billed and the amount.",
            "OpsFeeBilling", [new Column("Term", "Term", 90), new Column("Grade", "Grade", 70), new Column("Students", "Students", 60), new Column("Boarders", "Boarders", 60), new Column("Billed", "Billed", 100, true)], Period);
        yield return Listing("SC176", "Fee Arrears by Grade", "School", "Unpaid fee invoices as at the date per student, aged, grouped by grade.",
            "OpsFeeArrears", [new Column("Grade", "Grade", 60), new Column("Student", "Student", 150), new Column("Guardian", "Guardian", 140), new Column("Invoices", "Invoices", 55), new Column("Oldest", "Oldest due", 80),
             new Column("Band", "Age", 60), new Column("Outstanding", "Outstanding", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("SC177", "Fee Collection Report", "School", "Receipts from guardians in the period, with the students they pay for.",
            "OpsFeeCollections", [new Column("Date", "Date", 75), new Column("Receipt", "Receipt", 100), new Column("Guardian", "Guardian", 150), new Column("Students", "Students", 220), new Column("Amount", "Amount", 95, true)], Period);
        yield return Listing("SC178", "Bursary & Discount Report", "School", "Students on bursaries with type and value, and what that is worth against their grade's fees.",
            "OpsBursaries", [new Column("Student", "Student", 150), new Column("Grade", "Grade", 60), new Column("Type", "Type", 80), new Column("Rate", "Value", 70), new Column("Fees", "Grade fees", 90),
             new Column("Worth", "Bursary worth", 95, true)], []);
        yield return Listing("SC180", "Enrolment & Headcount", "School", "Per grade: enrolled, boarders, day scholars, bursary holders and students no longer enrolled.",
            "OpsEnrolment", [new Column("Grade", "Grade", 70), new Column("Enrolled", "Enrolled", 60), new Column("Boarders", "Boarders", 60), new Column("Day", "Day", 50), new Column("Bursary", "On bursary", 65),
             new Column("Left", "Not enrolled", 75)], []);
        yield return Listing("SC182", "Student Balances Listing", "School", "Every student with guardian, grade, enrolment and what is owed on their fee invoices at the date.",
            "OpsStudentBalances", [new Column("Admission", "Admission", 80), new Column("Student", "Student", 150), new Column("Grade", "Grade", 60), new Column("Guardian", "Guardian", 140),
             new Column("Status", "Status", 80), new Column("Balance", "Balance", 95, true)], [("AsAtDate", ParameterType.Date)]);

        // Construction / job costing.
        yield return Listing("JC186", "Job Cost Report by Cost Head", "JobCosting", "Per contract and cost head: cost to date and share of the contract's cost, against its budget.",
            "OpsJobCosts", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("CostHead", "Cost head", 110), new Column("Source", "Source", 90), new Column("Share", "Share", 60),
             new Column("Cost", "Cost to date", 100, true)], []);
        yield return Listing("JC189", "Cost to Complete & Forecast Final Cost", "JobCosting", "Per contract: value, budget, cost to date, % complete by certification, forecast final cost and the variance to budget.",
            "OpsCostToComplete", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Value", "Contract value", 100), new Column("Budget", "Budget", 95), new Column("Actual", "Cost to date", 95),
             new Column("Complete", "% complete", 70), new Column("Forecast", "Forecast final", 100), new Column("Variance", "Over / (under) budget", 105, true)], []);
        yield return Listing("JC190", "Retention Held by Contract", "JobCosting", "Retention held on each contract's certificates, with the contract status.",
            "OpsRetention", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Customer", "Customer", 140), new Column("Status", "Status", 70), new Column("Rate", "Retention %", 70),
             new Column("Certified", "Certified", 100), new Column("Retention", "Retention held", 100, true)], []);
        yield return Listing("JC197", "Contract Cash Flow", "JobCosting", "Per contract over the period: net certified (after retention) against costs incurred, and the net cash position.",
            "OpsContractCash", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Certified", "Net certified", 100), new Column("Costs", "Costs", 100), new Column("Net", "Net position", 100, true)], Period);

        // Sales order processing.
        yield return Listing("SO137", "Open Sales Orders & Allocation", "SalesOrders", "Open order lines with what is outstanding to despatch and the free stock that can cover it.",
            "OpsOpenOrders", [new Column("Order", "Order", 85), new Column("Date", "Date", 75), new Column("Customer", "Customer", 140), new Column("Item", "Item", 150), new Column("Ordered", "Ordered", 60),
             new Column("Despatched", "Despatched", 70), new Column("Outstanding", "Outstanding", 70), new Column("Free", "Free stock", 70), new Column("Cover", "Cover", 80), new Column("Value", "Outstanding value", 95, true)], []);
        yield return Listing("SO143", "Quotation Conversion Rate", "SalesOrders", "Quotations by month: count, converted to orders, value quoted, value converted and the conversion rate.",
            "OpsQuoteConversion", [new Column("Month", "Month", 70), new Column("Quotes", "Quotes", 55), new Column("Converted", "Converted", 65), new Column("Rate", "Rate", 60), new Column("Quoted", "Value quoted", 100),
             new Column("ConvertedValue", "Value converted", 100, true)], Period);
    }
}
