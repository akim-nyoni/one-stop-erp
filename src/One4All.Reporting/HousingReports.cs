using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// The Housing, Land &amp; Estates report catalogue (brief §14 I-B, the H series), licensable and versioned on its own.
/// Each definition is a listing over a <c>Housing*</c> data command served by the housing report data provider.
/// </summary>
public static class HousingReports
{
    /// <summary>Every H-series definition shipped.</summary>
    /// <returns>The definitions, in H order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        // Land bank & acquisition.
        yield return Listing("H1", "Land Bank Register", "Housing", "All parcels with area, cost, status and mapped area.", "HousingLandBank",
            [new Column("Parcel", "Parcel", 100), new Column("Description", "Description", 180), new Column("AreaM2", "Area m²", 80, true), new Column("MappedM2", "Mapped m²", 80),
             new Column("Status", "Status", 80), new Column("Cost", "Cost", 100, true)], []);
        yield return Listing("H2", "Land Acquisition Register", "Housing", "Acquisitions by method with counterparty, council resolution, status and cost.", "HousingAcquisitions",
            [new Column("Acquisition", "Acquisition", 90), new Column("Parcel", "Parcel", 90), new Column("Method", "Method", 90), new Column("Counterparty", "Counterparty", 150),
             new Column("Resolution", "Resolution", 120), new Column("Status", "Status", 80), new Column("Cost", "Cost", 100, true)], []);
        yield return Listing("H4", "Due Diligence Outstanding", "Housing", "Open acquisitions and the due-diligence items blocking each.", "HousingDueDiligence",
            [new Column("Acquisition", "Acquisition", 90), new Column("Parcel", "Parcel", 90), new Column("Outstanding", "What blocks completion", 420)], []);
        yield return Listing("H5", "Compulsory Acquisition Compensation Register", "Housing", "Compensation claims with provision, settlement and status.", "HousingCompensation",
            [new Column("Claim", "Claim", 90), new Column("Parcel", "Parcel", 90), new Column("Claimant", "Claimant", 160), new Column("Lodged", "Lodged", 80),
             new Column("Status", "Status", 80), new Column("Provision", "Provision", 90, true), new Column("Settled", "Settled", 90, true)], []);

        // Planning, survey & servicing.
        yield return Listing("H10", "Layout Plans Register & Approval Status", "Housing", "Layouts with the version in force, pending amendments and approval turnaround.", "HousingLayouts",
            [new Column("Plan", "Layout", 90), new Column("Parcel", "Parcel", 90), new Column("Version", "Version", 60), new Column("Status", "Status", 110),
             new Column("Stands", "Stands", 60), new Column("TurnaroundDays", "Turnaround days", 90)], []);
        yield return Listing("H14", "Servicing Cost Apportionment Schedule", "Housing", "Servicing cost per stand by contract and basis.", "HousingServicing",
            [new Column("Stand", "Stand", 90), new Column("Contract", "Contract", 110), new Column("Basis", "Basis", 100), new Column("Date", "Date", 80),
             new Column("Journal", "Journal", 100), new Column("Amount", "Amount", 100, true)], []);
        yield return Listing("H15", "Cost per Stand / Selling Price Build-up", "Housing", "Land and servicing cost, overhead, levy and margin to price, with margin %.", "HousingPriceBuildUp",
            [new Column("Stand", "Stand", 80), new Column("Scope", "Rule", 70), new Column("Cost", "Cost", 90, true), new Column("Overhead", "Overhead", 80, true),
             new Column("Levy", "Levy", 80, true), new Column("Margin", "Margin", 80, true), new Column("MarginPercent", "Margin %", 60), new Column("Price", "Price", 90, true)],
            [("AsAtDate", ParameterType.Date)]);

        // Waiting list.
        yield return Listing("H18", "Waiting List Register", "Housing", "Active applicants by category in seniority order with score and position.", "HousingWaitingList",
            [new Column("Category", "Category", 100), new Column("Position", "Pos.", 45), new Column("WaitingListNumber", "WL number", 100), new Column("Name", "Applicant", 170),
             new Column("Registered", "Registered", 80), new Column("Score", "Score", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H20", "Waiting List Age Analysis", "Housing", "How long active applicants have been waiting, by category.", "HousingWaitingAgeing",
            [new Column("Category", "Category", 110), new Column("UnderOne", "< 1 yr", 60), new Column("OneToThree", "1–3", 60), new Column("ThreeToFive", "3–5", 60),
             new Column("FiveToTen", "5–10", 60), new Column("TenPlus", "10+", 60), new Column("Total", "Total", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H26", "Registration Date Change Audit", "Housing", "Every requested change to a seniority date: who asked, who decided, why.", "HousingRegistrationChanges",
            [new Column("WaitingListNumber", "WL number", 100), new Column("OldDate", "From", 80), new Column("NewDate", "To", 80), new Column("Reason", "Reason", 200),
             new Column("Status", "Status", 80), new Column("RequestedBy", "Requested by", 80), new Column("DecidedBy", "Decided by", 80)], []);

        // Allocation.
        yield return Listing("H31", "Allocation Register", "Housing", "Allocations with stand, beneficiary, certificate, frozen price and approval.", "HousingAllocations",
            [new Column("Certificate", "Certificate", 100), new Column("Run", "Run", 90), new Column("Stand", "Stand", 80), new Column("WaitingListNumber", "WL number", 90),
             new Column("Beneficiary", "Beneficiary", 150), new Column("Status", "Status", 80), new Column("ApprovedOn", "Approved", 80), new Column("Price", "Price", 90, true)], []);
        yield return Listing("H35", "Offers Outstanding / Expiring / Expired", "Housing", "Open offers with the acceptance deadline countdown.", "HousingOffers",
            [new Column("Certificate", "Certificate", 100), new Column("Stand", "Stand", 80), new Column("Beneficiary", "Beneficiary", 160), new Column("Expires", "Expires", 80),
             new Column("DaysLeft", "Days left", 70), new Column("State", "State", 80)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H38", "Appeals Register & Outcomes", "Housing", "Appeals lodged against allocation runs with hearing and outcome.", "HousingAppeals",
            [new Column("Appeal", "Appeal", 90), new Column("WaitingListNumber", "Appellant", 100), new Column("Run", "Run", 90), new Column("Lodged", "Lodged", 80),
             new Column("Status", "Status", 80), new Column("Hearing", "Hearing", 80), new Column("Outcome", "Outcome", 220)], []);
        yield return Listing("H39", "Double Allocation Exception Report", "Housing", "Stands promised through more than one route — must always be empty; any row is an incident.", "HousingDoubleAllocations",
            [new Column("Stand", "Stand", 90), new Column("Claims", "Claims", 60), new Column("Routes", "Promised through", 420)], []);

        // Agreements & title readiness.
        yield return Listing("H49", "Fully Paid / Ready for Transfer", "Housing", "Sold stands whose holders owe nothing and have no title application — the most actionable list.", "HousingReadyForTitle",
            [new Column("Stand", "Stand", 80), new Column("Holder", "Holder", 170), new Column("Status", "Stand status", 90), new Column("SoldOn", "Sold", 80), new Column("Price", "Price", 90, true)], []);

        // Change of ownership, cession & title.
        yield return Listing("H56", "Ownership History / Chain of Title per Stand", "Housing", "Every holder of a stand in sequence with the change type, date and consideration, and whether the hash chain verifies.", "HousingChainOfTitle",
            [new Column("Stand", "Stand", 80), new Column("Sequence", "#", 35), new Column("ChangeType", "Change", 90), new Column("Holder", "Holder", 170), new Column("Effective", "Effective", 80),
             new Column("ChainVerified", "Chain", 60), new Column("Consideration", "Consideration", 100, true)], [("StandNumber", ParameterType.String)]);
        yield return Listing("H57", "Change of Ownership Register", "Housing", "Completed transfers by type with parties, consideration and clearance relied on.", "HousingTransfers",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Type", "Type", 90), new Column("From", "From", 140), new Column("To", "To", 140),
             new Column("Completed", "Completed", 80), new Column("Clearance", "Clearance", 90), new Column("Consideration", "Consideration", 100, true)], []);
        yield return Listing("H58", "Transfers in Progress / Pending Approval", "Housing", "Open transfer applications with status, approvals and age.", "HousingTransfersOpen",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Type", "Type", 90), new Column("Status", "Status", 80),
             new Column("Approvals", "Approvals", 70), new Column("DaysOpen", "Days open", 70), new Column("Waiting", "Waiting for", 220)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H59", "Transfers Blocked by Outstanding Rates or Housing Arrears", "Housing", "Open transfers failing a live check, with what is needed to clear them.", "HousingTransfersBlocked",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Check", "Check", 140), new Column("Detail", "What is needed", 360)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H64", "Title Deed Pipeline Report", "Housing", "Open title applications with stage, days at the stage, days since applied and conveyancer.", "HousingTitlePipeline",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Stage", "Stage", 110), new Column("DaysAtStage", "Days at stage", 80),
             new Column("DaysSinceApplied", "Since applied", 80), new Column("Conveyancer", "Conveyancer", 160)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H65", "Title Deeds Held but Uncollected", "Housing", "Deeds in council custody not yet handed over, aged, with the owner's contact — the chase list.", "HousingDeedsUncollected",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Deed", "Deed", 100), new Column("Owner", "Owner", 160), new Column("Contact", "Contact", 160),
             new Column("DaysHeld", "Days held", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H66", "Deeds Register", "Housing", "Registered deeds with registration date and current custody.", "HousingDeeds",
            [new Column("Deed", "Deed", 110), new Column("Stand", "Stand", 80), new Column("Registered", "Registered", 80), new Column("Custody", "Custody", 220)], []);
        yield return Listing("H68", "Caveats, Blocks & Disputes Register", "Housing", "Every block placed: reason, particulars, document, who placed and lifted it, age.", "HousingBlocks",
            [new Column("Stand", "Stand", 80), new Column("Reason", "Reason", 110), new Column("Detail", "Particulars", 200), new Column("Document", "Document", 90),
             new Column("PlacedOn", "Placed", 80), new Column("State", "State", 110), new Column("AgeDays", "Age (days)", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H69", "Stands with No Recorded Owner / Broken Ownership Chain", "Housing", "Data-integrity exceptions — must be empty.", "HousingBrokenChains",
            [new Column("Stand", "Stand", 90), new Column("Problem", "Problem", 460)], []);
        yield return Listing("H70", "Sectional Title Unit Register & Participation Quotas", "Housing", "Units of every sectional scheme with floor area, quota and owner.", "HousingSectionalUnits",
            [new Column("Scheme", "Scheme", 90), new Column("Building", "Building", 150), new Column("Unit", "Unit", 60), new Column("FloorArea", "Floor m²", 70), new Column("Quota", "Quota %", 70),
             new Column("Owner", "Owner", 170)], []);
    }
}
