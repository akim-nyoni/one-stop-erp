using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// The Municipality / Council report catalogue (brief §14 I, items 144–172; codes are M plus the brief's item number).
/// Each definition is a listing over a <c>Muni*</c> data command served by the municipal report data provider; items the
/// receipting (RC) series already covers are not repeated here.
/// </summary>
public static class MunicipalReports
{
    /// <summary>Every M-series definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        // Billing.
        yield return Listing("M146", "Billing Run Summary & Control", "Municipal", "Each billing run: service, period, ward, lines billed and excluded, total billed against the previous run, proposer and approver.",
            "MuniBillingRuns", [new Column("Run", "Run", 90), new Column("Service", "Service", 80), new Column("Period", "Period", 70), new Column("Ward", "Ward", 60), new Column("Status", "Status", 70),
             new Column("Lines", "Lines", 50), new Column("Excluded", "Excluded", 60), new Column("Previous", "Previous total", 95), new Column("Approver", "Approved by", 90),
             new Column("Billed", "Billed", 100, true)], []);
        yield return Listing("M147", "Billing Exceptions Report", "Municipal", "Lines of every billing run flagged as already billed, nothing to bill, failed or a large change on last time.",
            "MuniBillingExceptions", [new Column("Run", "Run", 90), new Column("Stand", "Stand", 80), new Column("Exceptions", "Exceptions", 160), new Column("Message", "Message", 220),
             new Column("Included", "Included", 60), new Column("Previous", "Previous", 85), new Column("Charge", "Charge", 90, true)], []);
        yield return Listing("M148", "Aged Debtors by Ward, Category and Service", "Municipal", "Ratepayers' open items as at the date, aged 30/60/90/120+, grouped by ward, consumer category and service.",
            "MuniAgedDebtors", [new Column("Ward", "Ward", 60), new Column("Category", "Category", 90), new Column("Service", "Service", 80), new Column("Accounts", "Accounts", 60),
             new Column("Current", "Current", 85), new Column("D30", "1–30", 85), new Column("D60", "31–60", 85), new Column("D90", "61–90", 85), new Column("D120", "91–120", 85),
             new Column("Over120", "120+", 85), new Column("Total", "Total", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M151", "Valuation Roll & Supplementary Roll", "Municipal", "Every valuation entry (general, supplementary, objection) with its effective date, and objections with their outcome.",
            "MuniValuationRoll", [new Column("Stand", "Stand", 80), new Column("Ward", "Ward", 60), new Column("Kind", "Kind", 90), new Column("Reference", "Reference", 100),
             new Column("EffectiveFrom", "Effective", 75), new Column("Objection", "Objection", 150), new Column("Value", "Value", 110, true)], []);
        yield return Listing("M152", "Property Register (full)", "Municipal", "Every property with owner(s), ratepayer, services and tariffs, meters, valuation and ward.",
            "MuniPropertyRegister", [new Column("Stand", "Stand", 80), new Column("Ward", "Ward", 60), new Column("Address", "Address", 140), new Column("Owners", "Owners", 150),
             new Column("Ratepayer", "Ratepayer", 140), new Column("Services", "Services & tariffs", 220), new Column("Meters", "Meters", 110), new Column("Valuation", "Valuation", 100, true)], []);
        yield return Listing("M153", "Rates Clearance Certificate Register", "Municipal", "Certificates issued with property, validity, advance held and who issued them, and whether still valid at the date.",
            "MuniClearances", [new Column("Certificate", "Certificate", 90), new Column("Stand", "Stand", 80), new Column("Issued", "Issued", 75), new Column("ValidUntil", "Valid until", 75),
             new Column("State", "State", 70), new Column("IssuedBy", "Issued by", 90), new Column("AdvanceHeld", "Advance held", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M154", "Meter Reading Sheets / Route Books", "Municipal", "Each reading route in walking order: meter, stand, last reading and date, blank for the new reading.",
            "MuniReadingSheets", [new Column("Route", "Route", 80), new Column("Reader", "Reader", 100), new Column("Sequence", "Seq", 40), new Column("Meter", "Meter", 90), new Column("Stand", "Stand", 80),
             new Column("Address", "Address", 140), new Column("LastReading", "Last reading", 85), new Column("LastDate", "Read on", 75), new Column("NewReading", "New reading", 90)],
            [("RouteCode", ParameterType.String)]);
        yield return Listing("M155", "Meter Reading Exceptions", "Municipal", "Readings flagged high or low consumption, rollover, negative or tampered, and estimates, with whether they were accepted.",
            "MuniReadingExceptions", [new Column("Date", "Date", 75), new Column("Meter", "Meter", 90), new Column("Stand", "Stand", 80), new Column("Kind", "Kind", 70), new Column("Flags", "Flags", 170),
             new Column("Previous", "Previous", 80), new Column("Reading", "Reading", 80), new Column("Accepted", "Accepted", 60), new Column("Consumption", "Consumption", 90, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M156", "Consumption Analysis", "Municipal", "Metered consumption for the twelve months to the date against the twelve before, per meter with its category.",
            "MuniConsumption", [new Column("Stand", "Stand", 80), new Column("Meter", "Meter", 90), new Column("Service", "Service", 80), new Column("Category", "Category", 90),
             new Column("Readings", "Readings", 60), new Column("PriorYear", "Prior 12 months", 95), new Column("Change", "Change", 70), new Column("Consumption", "Last 12 months", 100, true)],
            [("AsAtDate", ParameterType.Date)]);

        // Meters, credit control and social relief.
        yield return Listing("M159", "Meter Register & Replacement History", "Municipal", "Every meter with stand, service, installation and removal, what replaced it, last actual reading and units estimated since.",
            "MuniMeters", [new Column("Meter", "Meter", 90), new Column("Stand", "Stand", 80), new Column("Service", "Service", 80), new Column("Installed", "Installed", 75), new Column("Removed", "Removed", 75),
             new Column("ReplacedBy", "Replaced by", 90), new Column("LastActual", "Last actual", 90), new Column("Status", "Status", 70), new Column("EstimatedSinceActual", "Estimated since actual", 110, true)], []);
        yield return Listing("M160", "Disconnection & Reconnection Lists", "Municipal", "Service orders to disconnect for arrears or reconnect after payment, with status, execution and the fee raised.",
            "MuniServiceOrders", [new Column("Order", "Order", 90), new Column("Kind", "Kind", 80), new Column("Stand", "Stand", 80), new Column("Ward", "Ward", 60), new Column("Service", "Service", 80),
             new Column("Raised", "Raised", 75), new Column("Status", "Status", 70), new Column("Executed", "Executed", 75), new Column("Fee", "Fee invoice", 90), new Column("Arrears", "Arrears", 95, true)], []);
        yield return Listing("M161", "Payment Arrangements Register (all debtors)", "Municipal", "Every payment arrangement: terms, status and standing as at the date.",
            "MuniArrangements", [new Column("Ward", "Ward", 60), new Column("Debtor", "Debtor", 150), new Column("Started", "Started", 75), new Column("Total", "Total", 85),
             new Column("Instalment", "Instalment", 80), new Column("Status", "Status", 70), new Column("DueToDate", "Due to date", 85), new Column("Paid", "Paid", 85), new Column("Missed", "Missed", 50),
             new Column("Shortfall", "Shortfall", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M162", "Indigent Register & Subsidy Cost", "Municipal", "Registered indigent households with income, subsidy rate, review date and the subsidy credited in the year to the date.",
            "MuniIndigent", [new Column("Stand", "Stand", 80), new Column("Ward", "Ward", 60), new Column("Income", "Household income", 95), new Column("Rate", "Subsidy %", 60),
             new Column("ApprovedFrom", "From", 75), new Column("ReviewDue", "Review due", 75), new Column("Status", "Status", 70), new Column("Subsidy", "Subsidy YTD", 95, true)], [("AsAtDate", ParameterType.Date)]);

        // Licensing, service delivery and tariffs.
        yield return Listing("M166", "Licences & Permits Register and Renewals Due", "Municipal", "Every licence with holder, expiry, days to expiry at the date, renewals and the latest inspection.",
            "MuniLicences", [new Column("Licence", "Licence", 90), new Column("Kind", "Kind", 80), new Column("Holder", "Holder", 140), new Column("Expires", "Expires", 75), new Column("Due", "Days to expiry", 75),
             new Column("Status", "Status", 70), new Column("Renewals", "Renewals", 55), new Column("Inspection", "Last inspection", 150), new Column("Fee", "Fee", 85, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M167", "Service Request / Fault Ticket SLA", "Municipal", "Every ticket with category, priority, response and resolution against their SLA deadlines.",
            "MuniTicketSla", [new Column("Ticket", "Ticket", 90), new Column("Category", "Category", 90), new Column("Priority", "Priority", 60), new Column("Reported", "Reported", 110),
             new Column("Status", "Status", 70), new Column("Response", "Response", 110), new Column("Resolution", "Resolution", 110), new Column("Hours", "Hours open", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("M168", "Tariff Schedule", "Municipal", "Every tariff with its type, category, ward, effective date, charges, blocks and seasonal rates.",
            "MuniTariffs", [new Column("Tariff", "Tariff", 80), new Column("Name", "Name", 140), new Column("Service", "Service", 80), new Column("Type", "Type", 90), new Column("Category", "Category", 85),
             new Column("Ward", "Ward", 55), new Column("Effective", "Effective", 75), new Column("Charges", "Charges", 220), new Column("Blocks", "Blocks / seasons", 200), new Column("Active", "Active", 50)], []);
    }
}
