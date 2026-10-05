using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Fixed asset (brief §14 M, codes FA + item number) and tax &amp; statutory (§14 N, codes TX + item number) reports not
/// already in the core catalogue, over <c>Fatx*</c> data commands served by the asset and tax report data provider.
/// </summary>
public static class AssetTaxReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every FA- and TX-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("FA207", "Depreciation Schedule (actual)", "FixedAssets", "Depreciation charged in the period, by month and asset, from the posted depreciation runs.",
            "FatxDepreciation", [new Column("Month", "Month", 70), new Column("Asset", "Asset", 90), new Column("Description", "Description", 180), new Column("Class", "Class", 90),
             new Column("Charge", "Charge", 95, true)], Period);
        yield return Listing("FA208", "Asset Additions & Transfers", "FixedAssets", "Assets acquired in the period, and changes of location or custodian recorded in the audit trail.",
            "FatxAdditions", [new Column("Kind", "Kind", 80), new Column("Asset", "Asset", 90), new Column("Description", "Description", 180), new Column("Class", "Class", 90), new Column("Date", "Date", 115),
             new Column("Detail", "Detail", 200), new Column("Cost", "Cost", 95, true)], Period);
        yield return Listing("FA209", "Asset Movement Schedule (note)", "FixedAssets", "Per asset class: cost and accumulated depreciation brought forward, additions, disposals, charge and carried forward, and the net book value.",
            "FatxMovement", [new Column("Class", "Class", 100), new Column("CostOpening", "Cost b/f", 90), new Column("Additions", "Additions", 85), new Column("Disposals", "Disposals", 85), new Column("CostClosing", "Cost c/f", 90),
             new Column("DepOpening", "Dep b/f", 90), new Column("Charge", "Charge", 85), new Column("DepDisposals", "On disposals", 85), new Column("DepClosing", "Dep c/f", 90), new Column("Nbv", "NBV", 95, true)], Period);
        yield return Listing("FA210", "Asset Verification / Count Variance", "FixedAssets", "Each verification in the period: every asset expected or seen, where the register has it and where it was found, its condition and result, with the net book value at risk.",
            "FatxVerification", [new Column("Count", "Count", 85), new Column("Date", "Date", 75), new Column("Asset", "Asset", 90), new Column("Description", "Description", 160), new Column("Expected", "Register location", 100),
             new Column("Found", "Found at", 100), new Column("Condition", "Condition", 90), new Column("Result", "Result", 90), new Column("Nbv", "NBV", 90, true)], Period);
        yield return Listing("FA211", "Capital Work in Progress", "FixedAssets", "Each capital project: budget, cost brought forward, added and capitalised in the period, the CWIP balance carried forward and the share of budget spent.",
            "FatxCwip", [new Column("Project", "Project", 80), new Column("Name", "Name", 160), new Column("Opened", "Opened", 75), new Column("Status", "Status", 110), new Column("Budget", "Budget", 90),
             new Column("Opening", "B/f", 90), new Column("Added", "Added", 90), new Column("Capitalised", "Capitalised", 90), new Column("Spent", "Spent %", 60), new Column("Closing", "CWIP c/f", 95, true)], Period);
        yield return Listing("FA212", "Fully Depreciated Assets Still in Use", "FixedAssets", "Assets written down to residual value that are still held, with where they are and who holds them.",
            "FatxFullyDepreciated", [new Column("Asset", "Asset", 90), new Column("Description", "Description", 180), new Column("Class", "Class", 90), new Column("Acquired", "Acquired", 75),
             new Column("Location", "Location", 100), new Column("Custodian", "Custodian", 100), new Column("Cost", "Cost", 95, true)], []);
        yield return Listing("FA213", "Insurance & Warranty Expiry", "FixedAssets", "Held assets whose insurance or warranty has lapsed or ends within the window, uninsured assets, and cover below net book value.",
            "FatxInsurance", [new Column("Asset", "Asset", 90), new Column("Description", "Description", 160), new Column("Insurer", "Insurer / policy", 140), new Column("InsuranceExpiry", "Cover ends", 75),
             new Column("WarrantyExpiry", "Warranty ends", 75), new Column("Issue", "Issue", 200), new Column("Insured", "Sum insured", 90), new Column("Nbv", "NBV", 90, true)],
            [("AsAtDate", ParameterType.Date), ("Days", ParameterType.Integer)]);
        yield return Listing("FA214", "Maintenance Schedule & History", "FixedAssets", "Planned tasks with when they were last done and are next due (overdue flagged), then the maintenance logged in the period.",
            "FatxMaintenance", [new Column("Section", "Section", 70), new Column("Asset", "Asset", 90), new Column("Description", "Asset description", 140), new Column("Task", "Task / work", 180),
             new Column("LastDone", "Last done / date", 80), new Column("NextDue", "Next due / provider", 100), new Column("Status", "Status", 70), new Column("Cost", "Cost", 90, true)],
            [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date), ("AsAtDate", ParameterType.Date)]);
        yield return Listing("TX216", "VAT Detail / Audit Report", "Tax", "Every posted line carrying a tax code in the period, by code, with its journal and document.",
            "FatxVatDetail", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 100), new Column("Document", "Document", 130), new Column("TaxCode", "Tax code", 65), new Column("Account", "Account", 70),
             new Column("Narrative", "Narrative", 180), new Column("Amount", "Amount (Cr +)", 95, true)], Period);
        yield return Listing("TX217", "VAT Reconciliation", "Tax", "Per tax code: movement on its output and input tax accounts in the period against the tax-coded lines, with the difference.",
            "FatxVatRecon", [new Column("TaxCode", "Tax code", 70), new Column("Name", "Name", 140), new Column("Output", "Output account", 100), new Column("Input", "Input account", 100), new Column("Coded", "Coded lines", 100),
             new Column("Difference", "Difference", 95, true)], Period);
        yield return Listing("TX218", "Exempt, Zero-rated & Out-of-scope Supplies", "Tax", "Lines in the period under zero-rated, exempt and out-of-scope tax codes, totalled per code.",
            "FatxNonStandard", [new Column("TaxCode", "Tax code", 70), new Column("Name", "Name", 160), new Column("Type", "Type", 90), new Column("Lines", "Lines", 55), new Column("Amount", "Amount", 100, true)], Period);
        yield return Listing("TX219", "Reverse Charge & Import VAT", "Tax", "Lines in the period under reverse-charge and deemed (import) tax codes: the output tax self-assessed, the input tax claimed and what is left payable.",
            "FatxReverseCharge", [new Column("Date", "Date", 75), new Column("Journal", "Journal", 95), new Column("Document", "Document", 130), new Column("TaxCode", "Tax code", 70), new Column("Account", "Account", 70),
             new Column("Side", "Side", 70), new Column("Amount", "Payable effect", 100, true)], Period);
        yield return Listing("TX220", "Withholding Tax Return", "Tax", "Withholding certificates by month and tax code: count, gross paid, tax withheld and how much has been remitted.",
            "FatxWhtReturn", [new Column("Month", "Month", 70), new Column("TaxCode", "Tax code", 70), new Column("Certificates", "Certificates", 75), new Column("Gross", "Gross", 100), new Column("Remitted", "Remitted", 100),
             new Column("Withheld", "Withheld", 100, true)], Period);
        yield return Listing("TX224", "Annual Financial Statements Pack", "Tax", "Statement of financial position and profit or loss by report category for the year to the date, with the prior year's comparatives and the note each line is disclosed in.",
            "FatxAfs", [new Column("Statement", "Statement", 150), new Column("Line", "Line", 200), new Column("Note", "Note", 50), new Column("Prior", "Prior year", 110), new Column("Current", "Current year", 110, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("TX225", "Audit File Export (SAF-T style)", "Tax", "Every posted journal line in the period with its period, source, document, account, tax code, trading party, currency amounts and base debit / credit — the GL audit file.",
            "FatxAuditFile", [new Column("Journal", "Journal", 95), new Column("Date", "Date", 75), new Column("Period", "Period", 55), new Column("Source", "Source", 50), new Column("Document", "Document", 130),
             new Column("Account", "Account", 65), new Column("TaxCode", "Tax", 50), new Column("Party", "Party", 90), new Column("Currency", "Ccy", 40), new Column("Transaction", "Transaction", 95),
             new Column("Debit", "Debit", 95), new Column("Credit", "Credit", 95), new Column("Narrative", "Narrative", 180)], Period);
    }
}
