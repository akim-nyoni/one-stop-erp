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
        yield return Listing("SC179", "Sponsor Account Statement", "School", "Per sponsor: the students they sponsor, fees billed to them and receipts in the period, and the balance they owe at the end of it.",
            "OpsSponsors", [new Column("Sponsor", "Sponsor", 150), new Column("Students", "Students", 220), new Column("Billed", "Billed", 95), new Column("Received", "Received", 95), new Column("Balance", "Balance", 95, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SC180", "Enrolment & Headcount", "School", "Per grade: enrolled, boarders, day scholars, bursary holders and students no longer enrolled.",
            "OpsEnrolment", [new Column("Grade", "Grade", 70), new Column("Enrolled", "Enrolled", 60), new Column("Boarders", "Boarders", 60), new Column("Day", "Day", 50), new Column("Bursary", "On bursary", 65),
             new Column("Left", "Not enrolled", 75)], []);
        yield return Listing("SC181", "Deferred Fee Income Schedule", "School", "Fees billed in advance per invoice: billed, recognised to date as the term runs, and still deferred.",
            "OpsDeferredFees", [new Column("Invoice", "Invoice", 100), new Column("Description", "Description", 200), new Column("Term", "Term", 160), new Column("Billed", "Billed", 90), new Column("Recognised", "Recognised", 90),
             new Column("Deferred", "Deferred", 95, true)], []);
        yield return Listing("SC182", "Student Balances Listing", "School", "Every student with guardian, grade, enrolment and what is owed on their fee invoices at the date.",
            "OpsStudentBalances", [new Column("Admission", "Admission", 80), new Column("Student", "Student", 150), new Column("Grade", "Grade", 60), new Column("Guardian", "Guardian", 140),
             new Column("Status", "Status", 80), new Column("Balance", "Balance", 95, true)], [("AsAtDate", ParameterType.Date)]);

        // Construction / job costing.
        yield return Listing("SC183", "Transport Route Billing & Utilisation", "School", "Per route: seats, enrolled riders, utilisation, the fee and what was billed for transport in the period.",
            "OpsTransport", [new Column("Route", "Route", 70), new Column("Name", "Name", 140), new Column("Seats", "Seats", 50), new Column("Riders", "Riders", 50), new Column("Utilisation", "Utilisation", 70),
             new Column("Fee", "Fee / term", 80), new Column("Billed", "Billed", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SC184", "Boarding / Hostel Occupancy & Billing", "School", "Per hostel: beds, enrolled boarders, occupancy, the fee and what was billed for boarding in the period.",
            "OpsBoarding", [new Column("Hostel", "Hostel", 70), new Column("Name", "Name", 140), new Column("Beds", "Beds", 50), new Column("Boarders", "Boarders", 60), new Column("Occupancy", "Occupancy", 70),
             new Column("Fee", "Fee / term", 80), new Column("Billed", "Billed", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SC185", "Tuck Shop Sales & Student Account Spend", "School", "Per student over the period: wallet balance brought forward, top-ups, tuck-shop spend and the balance carried forward.",
            "OpsWallets", [new Column("Student", "Student", 170), new Column("Grade", "Grade", 60), new Column("Opening", "B/f", 85), new Column("TopUps", "Top-ups", 85), new Column("Spend", "Spend", 85),
             new Column("Days", "Days spent", 60), new Column("Closing", "C/f", 90, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("JC186", "Job Cost Report by Cost Head", "JobCosting", "Per contract and cost head: cost to date and share of the contract's cost, against its budget.",
            "OpsJobCosts", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("CostHead", "Cost head", 110), new Column("Source", "Source", 90), new Column("Share", "Share", 60),
             new Column("Cost", "Cost to date", 100, true)], []);
        yield return Listing("JC189", "Cost to Complete & Forecast Final Cost", "JobCosting", "Per contract: value, budget, cost to date, % complete by certification, forecast final cost and the variance to budget.",
            "OpsCostToComplete", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Value", "Contract value", 100), new Column("Budget", "Budget", 95), new Column("Actual", "Cost to date", 95),
             new Column("Complete", "% complete", 70), new Column("Forecast", "Forecast final", 100), new Column("Variance", "Over / (under) budget", 105, true)], []);
        yield return Listing("JC190", "Retention Held by Contract", "JobCosting", "Retention held on each contract's certificates, with the contract status.",
            "OpsRetention", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Customer", "Customer", 140), new Column("Status", "Status", 70), new Column("Rate", "Retention %", 70),
             new Column("Certified", "Certified", 100), new Column("Retention", "Retention held", 100, true)], []);
        yield return Listing("JC191", "Variation Order Register", "JobCosting", "Every variation submitted in the period: contract, status, decision, and the value and cost impact (approved impacts move the contract).",
            "OpsVariations", [new Column("Contract", "Contract", 85), new Column("Number", "VO", 70), new Column("Description", "Description", 180), new Column("Submitted", "Submitted", 75), new Column("Status", "Status", 70),
             new Column("Decided", "Decided", 75), new Column("CostImpact", "Cost impact", 90), new Column("ValueImpact", "Value impact", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("JC192", "Subcontractor Ledger, Certificates & Retention", "JobCosting", "Each subcontract's certificates in the period (gross, retention, payable) and retention released, with value, certified to date and retention held.",
            "OpsSubcontracts", [new Column("Contract", "Contract", 80), new Column("Subcontractor", "Subcontractor", 130), new Column("Subcontract", "Subcontract", 90), new Column("Document", "Certificate", 80),
             new Column("Date", "Date", 75), new Column("Gross", "Gross", 85), new Column("Retention", "Retention", 80), new Column("Position", "Certified / value · held", 160), new Column("Payable", "Payable", 90, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("JC193", "Labour & Timesheet Cost by Job", "JobCosting", "Hours and labour cost booked to each contract in the period, per employee and cost head.",
            "OpsTimesheets", [new Column("Contract", "Contract", 85), new Column("Employee", "Employee", 150), new Column("CostHead", "Cost head", 80), new Column("Days", "Days", 45), new Column("Hours", "Hours", 60),
             new Column("Rate", "Avg rate", 70), new Column("Cost", "Cost", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("JC194", "Plant Utilisation & Hire Recovery", "JobCosting", "Per item of plant over the period: hours worked against hours available, hire recovered from jobs, running costs, cost per hour and the net recovery.",
            "OpsPlant", [new Column("Plant", "Plant", 70), new Column("Name", "Name", 140), new Column("Hours", "Hours", 55), new Column("Available", "Available", 65), new Column("Utilisation", "Utilisation", 70),
             new Column("Recovered", "Hire recovered", 90), new Column("Running", "Running cost", 90), new Column("PerHour", "Cost / hour", 75), new Column("Net", "Net recovery", 95, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("HoursPerDay", ParameterType.Decimal)]);
        yield return Listing("JC195", "Materials Issued to Job", "JobCosting", "Stock issued to each contract in the period: date, item, quantity and cost, from the stock movements referenced to the contract.",
            "OpsJobMaterials", [new Column("Contract", "Contract", 85), new Column("Date", "Date", 75), new Column("Item", "Item", 170), new Column("Warehouse", "From", 70), new Column("Quantity", "Qty", 60),
             new Column("UnitCost", "Unit cost", 80), new Column("Cost", "Cost", 95, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("JC196", "BOQ vs Measured Quantities", "JobCosting", "Each bill-of-quantities line: billed quantity, measured to date, the over / under measure and the value of work measured.",
            "OpsBoq", [new Column("Contract", "Contract", 85), new Column("Item", "Item", 60), new Column("Description", "Description", 180), new Column("Unit", "Unit", 40), new Column("Billed", "BOQ qty", 65),
             new Column("Measured", "Measured", 65), new Column("Variance", "Over / (under)", 75), new Column("Complete", "% measured", 65), new Column("Value", "Value measured", 95, true)], []);
        yield return Listing("JC197", "Contract Cash Flow", "JobCosting", "Per contract over the period: net certified (after retention) against costs incurred, and the net cash position.",
            "OpsContractCash", [new Column("Contract", "Contract", 90), new Column("Name", "Name", 150), new Column("Certified", "Net certified", 100), new Column("Costs", "Costs", 100), new Column("Net", "Net position", 100, true)], Period);

        // Sales order processing.
        yield return Listing("SO137", "Open Sales Orders & Allocation", "SalesOrders", "Open order lines with what is outstanding to despatch and the free stock that can cover it.",
            "OpsOpenOrders", [new Column("Order", "Order", 85), new Column("Date", "Date", 75), new Column("Customer", "Customer", 140), new Column("Item", "Item", 150), new Column("Ordered", "Ordered", 60),
             new Column("Despatched", "Despatched", 70), new Column("Outstanding", "Outstanding", 70), new Column("Free", "Free stock", 70), new Column("Cover", "Cover", 80), new Column("Value", "Outstanding value", 95, true)], []);
        yield return Listing("SO140", "Despatch / Delivery Performance (OTIF)", "SalesOrders", "Orders due in the period: requested date, what was ordered and what was signed for by then — on time, in full and OTIF.",
            "OpsOtif", [new Column("Order", "Order", 90), new Column("Customer", "Customer", 140), new Column("Requested", "Requested", 75), new Column("Ordered", "Ordered", 60), new Column("OnTimeQty", "Delivered by date", 90),
             new Column("LastDelivered", "Last delivered", 80), new Column("OnTime", "On time", 55), new Column("InFull", "In full", 55), new Column("Otif", "OTIF", 45)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SO141", "Proof of Delivery Outstanding", "SalesOrders", "Delivery notes despatched by the date that have not been signed for, oldest first.",
            "OpsPodOutstanding", [new Column("Delivery", "Delivery", 90), new Column("Order", "Order", 90), new Column("Customer", "Customer", 140), new Column("Despatched", "Despatched", 75), new Column("Days", "Days out", 55),
             new Column("Vehicle", "Vehicle", 70), new Column("Driver", "Driver", 100), new Column("Quantity", "Quantity", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("SO142", "Delivery Schedule / Load Sheet", "SalesOrders", "Deliveries planned in the period by date, vehicle and route: what is on each load and its weight.",
            "OpsLoadSheet", [new Column("Date", "Date", 75), new Column("Vehicle", "Vehicle", 70), new Column("Route", "Route", 90), new Column("Driver", "Driver", 100), new Column("Delivery", "Delivery", 90),
             new Column("Order", "Order", 90), new Column("Customer", "Customer", 130), new Column("Items", "Items", 160), new Column("Weight", "Weight kg", 80, true)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SO143", "Quotation Conversion Rate", "SalesOrders", "Quotations by month: count, converted to orders, value quoted, value converted and the conversion rate.",
            "OpsQuoteConversion", [new Column("Month", "Month", 70), new Column("Quotes", "Quotes", 55), new Column("Converted", "Converted", 65), new Column("Rate", "Rate", 60), new Column("Quoted", "Value quoted", 100),
             new Column("ConvertedValue", "Value converted", 100, true)], Period);
    }
}
