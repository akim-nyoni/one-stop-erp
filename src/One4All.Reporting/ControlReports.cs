using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Audit, security and compliance reports (brief §14 P) and master data / system listings (§14 R) not already in the core
/// catalogue — codes are SYS plus the brief's item number — over <c>Sysr*</c> data commands served by the control report
/// data provider.
/// </summary>
public static class ControlReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every SYS-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        // Audit, security & compliance.
        yield return Listing("SYS237", "Permission Matrix", "System", "Every role with each permission it grants and the operations allowed.",
            "SysrPermissionMatrix", [new Column("Role", "Role", 120), new Column("Module", "Module", 90), new Column("Permission", "Permission", 170), new Column("Name", "Name", 180),
             new Column("Operations", "Operations", 200)], []);
        yield return Listing("SYS238", "User, Role & Group Membership", "System", "Every user with direct roles, groups, roles held through groups and individual overrides.",
            "SysrMembership", [new Column("User", "User", 100), new Column("Name", "Name", 140), new Column("Status", "Status", 80), new Column("Roles", "Direct roles", 160), new Column("Groups", "Groups", 140),
             new Column("GroupRoles", "Roles via groups", 160), new Column("Overrides", "Overrides", 60)], []);
        yield return Listing("SYS239", "Segregation of Duties Violations (all modules)", "System", "Every active user whose combined permissions break a separation-of-duties rule.",
            "SysrSod", [new Column("User", "User", 100), new Column("Rule", "Rule", 90), new Column("Conflict", "Conflict", 300), new Column("Severity", "Severity", 70)], []);
        yield return Listing("SYS240", "Master Data Change Report", "System", "Changes in the period to customers, suppliers, items, accounts, tariffs, properties, employees, price lists, users and roles.",
            "SysrMasterChanges", [new Column("When", "When", 115), new Column("User", "User", 90), new Column("Entity", "Record", 110), new Column("Id", "Id", 50), new Column("Action", "Action", 60),
             new Column("Changed", "Fields changed", 280)], Period);
        yield return Listing("SYS241", "Supervisor Override Report", "System", "Every override authorised by a supervisor in the period across the modules: POS price overrides, refunds and no-sales, and manual weighbridge weights.",
            "SysrOverrides", [new Column("When", "When", 115), new Column("Module", "Module", 80), new Column("Override", "Override", 120), new Column("Detail", "Detail", 220), new Column("Operator", "Operator", 90),
             new Column("AuthorisedBy", "Authorised by", 90), new Column("Reason", "Reason", 160)], [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)]);
        yield return Listing("SYS242", "Back-dated Transactions", "System", "Journals entered in the period with a posting date more than the given number of days before they were entered.",
            "SysrBackdated", [new Column("Journal", "Journal", 100), new Column("Posting", "Posting date", 80), new Column("Entered", "Entered", 80), new Column("DaysBack", "Days back", 60),
             new Column("Source", "Source", 60), new Column("Document", "Document", 120), new Column("PreparedBy", "Prepared by", 90), new Column("Narrative", "Narrative", 200)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("Days", ParameterType.Integer)]);
        yield return Listing("SYS243", "Deleted & Voided Documents", "System", "Records deleted or retired in the period, and journals reversed or cancelled, with who and why.",
            "SysrDeleted", [new Column("When", "When", 115), new Column("Kind", "Kind", 90), new Column("Entity", "Record", 120), new Column("Id", "Id / number", 100), new Column("User", "User", 90),
             new Column("Reason", "Reason", 220)], Period);
        yield return Listing("SYS244", "Document Numbering Gaps", "System", "Missing runs in each year's journal numbering.",
            "SysrGaps", [new Column("Year", "Year", 50), new Column("From", "From", 70), new Column("To", "To", 70), new Column("Missing", "Missing", 60), new Column("Description", "Description", 220)], []);
        yield return Listing("SYS245", "Unposted & Deferred Batches", "System", "Journals still draft, deferred or awaiting approval, with who prepared them.",
            "SysrUnposted", [new Column("Journal", "Journal", 100), new Column("Posting", "Posting date", 80), new Column("Status", "Status", 110), new Column("Source", "Source", 60),
             new Column("Document", "Document", 120), new Column("PreparedBy", "Prepared by", 90), new Column("Narrative", "Narrative", 200)], []);
        yield return Listing("SYS247", "Audit Trail Hash-Chain Verification", "System", "Recomputes the audit trail's hash chain for the company and for system-wide entries and says whether it is intact or where it first breaks.",
            "SysrHashChain", [new Column("Scope", "Scope", 120), new Column("Checked", "Entries checked", 90), new Column("Intact", "Intact", 60), new Column("FirstBroken", "First broken entry", 100),
             new Column("Message", "Result", 300)], []);
        yield return Listing("SYS248", "Licence Installation History", "System", "Licences installed, when and by whom, and which is active (token fingerprint only).",
            "SysrLicences", [new Column("Installed", "Installed", 115), new Column("By", "By", 90), new Column("Active", "Active", 50), new Column("Fingerprint", "Token fingerprint", 140)], []);
        yield return Listing("SYS250", "Report Print & Export Log", "System", "Every report run in the period: who, what, format, size and copy number.",
            "SysrReportRuns", [new Column("When", "When", 115), new Column("Report", "Report", 80), new Column("User", "User", 90), new Column("Purpose", "Purpose", 70), new Column("Format", "Format", 55),
             new Column("Bytes", "Bytes", 70), new Column("Copy", "Copy", 45), new Column("Parameters", "Parameters", 220)], Period);

        // Master data & system listings.
        yield return Listing("SYS258", "Price List Listing", "System", "Every price list with its items, quantity breaks and prices.",
            "SysrPriceLists", [new Column("PriceList", "Price list", 120), new Column("Currency", "Currency", 55), new Column("Status", "Status", 80), new Column("Item", "Item", 170), new Column("MinimumQuantity", "From qty", 65),
             new Column("UnitPrice", "Unit price", 90)], []);
        yield return Listing("SYS259", "Exchange Rate History", "System", "Every exchange rate captured, by currency pair and type, with its effective date.",
            "SysrRates", [new Column("From", "From", 50), new Column("To", "To", 50), new Column("Type", "Type", 70), new Column("Effective", "Effective", 80), new Column("Quotation", "Quotation", 80),
             new Column("Rate", "Rate", 100)], []);
        yield return Listing("SYS260", "Number Sequence Status", "System", "Every document sequence: prefix, padding, yearly reset, gapless flag and the next number per year.",
            "SysrSequences", [new Column("Document", "Document type", 110), new Column("Prefix", "Prefix", 70), new Column("Branch", "Branch", 55), new Column("Padding", "Padding", 55),
             new Column("Yearly", "Resets yearly", 70), new Column("Gapless", "Gapless", 60), new Column("Next", "Next number(s)", 220)], []);
        yield return Listing("SYS261", "Company Configuration Listing", "System", "Every setting of the company record.",
            "SysrCompany", [new Column("Setting", "Setting", 220), new Column("Value", "Value", 300)], []);
        yield return Listing("SYS262", "Report Definition Inventory", "System", "Every report definition held — system and custom — with scope, version and last change.",
            "SysrReportInventory", [new Column("Code", "Code", 80), new Column("Name", "Name", 220), new Column("Module", "Module", 90), new Column("Scope", "Scope", 70), new Column("Version", "Version", 55),
             new Column("Changed", "Last changed", 115)], []);
        yield return Listing("SYS263", "Scheduled Job Status & Delivery Log", "System", "Each scheduled job's state and last result, then report deliveries in the period.",
            "SysrJobs", [new Column("Kind", "Kind", 70), new Column("Name", "Job / subscription", 150), new Column("When", "When", 115), new Column("Status", "Status", 80), new Column("Detail", "Detail", 280)], Period);
    }
}
