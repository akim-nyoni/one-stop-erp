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
        yield return Listing("H3", "Land Acquisition Cost Build-up per Parcel", "Housing", "Each acquisition's cost by element with the total and cost per m².", "HousingAcquisitionCosts",
            [new Column("Acquisition", "Acquisition", 90), new Column("Parcel", "Parcel", 90), new Column("Element", "Element", 140), new Column("PerM2", "Per m²", 70), new Column("Amount", "Amount", 100, true)], []);
        yield return Listing("H4","Due Diligence Outstanding", "Housing", "Open acquisitions and the due-diligence items blocking each.", "HousingDueDiligence",
            [new Column("Acquisition", "Acquisition", 90), new Column("Parcel", "Parcel", 90), new Column("Outstanding", "What blocks completion", 420)], []);
        yield return Listing("H5", "Compulsory Acquisition Compensation Register", "Housing", "Compensation claims with provision, settlement and status.", "HousingCompensation",
            [new Column("Claim", "Claim", 90), new Column("Parcel", "Parcel", 90), new Column("Claimant", "Claimant", 160), new Column("Lodged", "Lodged", 80),
             new Column("Status", "Status", 80), new Column("Provision", "Provision", 90, true), new Column("Settled", "Settled", 90, true)], []);

        // Planning, survey & servicing.
        yield return Listing("H10", "Layout Plans Register & Approval Status", "Housing", "Layouts with the version in force, pending amendments and approval turnaround.", "HousingLayouts",
            [new Column("Plan", "Layout", 90), new Column("Parcel", "Parcel", 90), new Column("Version", "Version", 60), new Column("Status", "Status", 110),
             new Column("Stands", "Stands", 60), new Column("TurnaroundDays", "Turnaround days", 90)], []);
        yield return Listing("H11", "Stand Creation / Subdivision Report", "Housing", "Stands created from each parent parcel, with size, scheme, status and carried cost.", "HousingSubdivision",
            [new Column("Parcel", "Parent parcel", 100), new Column("Stand", "Stand", 80), new Column("SizeM2", "Size m²", 70), new Column("Scheme", "Scheme", 100), new Column("Status", "Status", 80),
             new Column("Cost", "Cost", 90, true)], []);
        yield return Listing("H14","Servicing Cost Apportionment Schedule", "Housing", "Servicing cost per stand by contract and basis.", "HousingServicing",
            [new Column("Stand", "Stand", 90), new Column("Contract", "Contract", 110), new Column("Basis", "Basis", 100), new Column("Date", "Date", 80),
             new Column("Journal", "Journal", 100), new Column("Amount", "Amount", 100, true)], []);
        yield return Listing("H15", "Cost per Stand / Selling Price Build-up", "Housing", "Land and servicing cost, overhead, levy and margin to price, with margin %.", "HousingPriceBuildUp",
            [new Column("Stand", "Stand", 80), new Column("Scope", "Rule", 70), new Column("Cost", "Cost", 90, true), new Column("Overhead", "Overhead", 80, true),
             new Column("Levy", "Levy", 80, true), new Column("Margin", "Margin", 80, true), new Column("MarginPercent", "Margin %", 60), new Column("Price", "Price", 90, true)],
            [("AsAtDate", ParameterType.Date)]);

        yield return Listing("H16", "Price List by Scheme & Stand Size Band", "Housing", "Every pricing rule with its scope, effective date, terms and approval — the price change history.", "HousingPriceList",
            [new Column("Scope", "Scope", 70), new Column("Applies", "Applies to", 160), new Column("Effective", "Effective", 80), new Column("Terms", "Overhead / levy / margin", 200),
             new Column("Approved", "Approved", 130)], []);

        // Waiting list.
        yield return Listing("H18", "Waiting List Register", "Housing", "Active applicants by category in seniority order with score and position.", "HousingWaitingList",
            [new Column("Category", "Category", 100), new Column("Position", "Pos.", 45), new Column("WaitingListNumber", "WL number", 100), new Column("Name", "Applicant", 170),
             new Column("Registered", "Registered", 80), new Column("Score", "Score", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H20", "Waiting List Age Analysis", "Housing", "How long active applicants have been waiting, by category.", "HousingWaitingAgeing",
            [new Column("Category", "Category", 110), new Column("UnderOne", "< 1 yr", 60), new Column("OneToThree", "1–3", 60), new Column("ThreeToFive", "3–5", 60),
             new Column("FiveToTen", "5–10", 60), new Column("TenPlus", "10+", 60), new Column("Total", "Total", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H19", "Waiting List Summary", "Housing", "Active applicants by category, income band and age band.", "HousingWaitingSummary",
            [new Column("Category", "Category", 110), new Column("IncomeBand", "Income band", 110), new Column("AgeBand", "Age band", 90), new Column("Applicants", "Applicants", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H22", "Priority Score Derivation", "Housing", "How each active applicant's points were computed, factor by factor.", "HousingScoreDerivation",
            [new Column("WaitingListNumber", "WL number", 100), new Column("Factor", "Factor", 130), new Column("Detail", "Applicant's figure", 140), new Column("Points", "Points", 70), new Column("Total", "Total score", 80)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H24", "Applicants Already Owning Property", "Housing", "Active applicants holding a council stand or owning rated property — disqualification candidates.", "HousingApplicantOwners",
            [new Column("WaitingListNumber", "WL number", 100), new Column("Name", "Applicant", 160), new Column("Holds", "Holds", 360)], []);
        yield return Listing("H26","Registration Date Change Audit", "Housing", "Every requested change to a seniority date: who asked, who decided, why.", "HousingRegistrationChanges",
            [new Column("WaitingListNumber", "WL number", 100), new Column("OldDate", "From", 80), new Column("NewDate", "To", 80), new Column("Reason", "Reason", 200),
             new Column("Status", "Status", 80), new Column("RequestedBy", "Requested by", 80), new Column("DecidedBy", "Decided by", 80)], []);

        // Allocation.
        yield return Listing("H30", "Proposed Allocation List", "Housing", "Pre-approval pairings of runs awaiting approval: stand, applicant, place and method.", "HousingProposedAllocations",
            [new Column("Run", "Run", 90), new Column("Method", "Method", 80), new Column("Sequence", "Place", 50), new Column("Stand", "Stand", 80), new Column("WaitingListNumber", "WL number", 100),
             new Column("Applicant", "Applicant", 160)], []);
        yield return Listing("H31","Allocation Register", "Housing", "Allocations with stand, beneficiary, certificate, frozen price and approval.", "HousingAllocations",
            [new Column("Certificate", "Certificate", 100), new Column("Run", "Run", 90), new Column("Stand", "Stand", 80), new Column("WaitingListNumber", "WL number", 90),
             new Column("Beneficiary", "Beneficiary", 150), new Column("Status", "Status", 80), new Column("ApprovedOn", "Approved", 80), new Column("Price", "Price", 90, true)], []);
        yield return Listing("H32", "Ballot / Draw Result Report", "Housing", "Ballot runs with the stored seed and input hash, re-verified now.", "HousingBallots",
            [new Column("Run", "Run", 90), new Column("Status", "Status", 80), new Column("Seed", "Seed", 120), new Column("InputHash", "Input hash", 200), new Column("Verified", "Re-verified", 160)], []);
        yield return Listing("H33", "Allocations by Scheme, Month and Approver", "Housing", "Approved allocations counted by scheme, approval month and approving officer.", "HousingAllocationsByMonth",
            [new Column("Scheme", "Scheme", 120), new Column("Month", "Month", 70), new Column("Approver", "Approver", 70), new Column("Allocations", "Allocations", 80)], []);
        yield return Listing("H35","Offers Outstanding / Expiring / Expired", "Housing", "Open offers with the acceptance deadline countdown.", "HousingOffers",
            [new Column("Certificate", "Certificate", 100), new Column("Stand", "Stand", 80), new Column("Beneficiary", "Beneficiary", 160), new Column("Expires", "Expires", 80),
             new Column("DaysLeft", "Days left", 70), new Column("State", "State", 80)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H36", "Declined, Lapsed & Cancelled Offers", "Housing", "Offers that did not proceed, with when and why.", "HousingFailedOffers",
            [new Column("Certificate", "Certificate", 100), new Column("Stand", "Stand", 80), new Column("Applicant", "Applicant", 160), new Column("Status", "Outcome", 80), new Column("DecidedOn", "On", 80),
             new Column("Reason", "Reason", 200)], []);
        yield return Listing("H38","Appeals Register & Outcomes", "Housing", "Appeals lodged against allocation runs with hearing and outcome.", "HousingAppeals",
            [new Column("Appeal", "Appeal", 90), new Column("WaitingListNumber", "Appellant", 100), new Column("Run", "Run", 90), new Column("Lodged", "Lodged", 80),
             new Column("Status", "Status", 80), new Column("Hearing", "Hearing", 80), new Column("Outcome", "Outcome", 220)], []);
        yield return Listing("H39", "Double Allocation Exception Report", "Housing", "Stands promised through more than one route — must always be empty; any row is an incident.", "HousingDoubleAllocations",
            [new Column("Stand", "Stand", 90), new Column("Claims", "Claims", 60), new Column("Routes", "Promised through", 420)], []);

        yield return Listing("H40", "Beneficiaries with More Than One Stand", "Housing", "People (by national ID) holding or allocated more than one stand across schemes.", "HousingMultipleStands",
            [new Column("IdNumber", "National ID", 110), new Column("Name", "Name", 160), new Column("Stands", "Stands", 60), new Column("Detail", "Held / allocated", 300)], []);
        yield return Listing("H41", "Allocation-to-Agreement Conversion", "Housing", "Accepted allocations with no agreement or instalment sale yet, with age since acceptance.", "HousingUnconverted",
            [new Column("Certificate", "Certificate", 100), new Column("Stand", "Stand", 80), new Column("Beneficiary", "Beneficiary", 160), new Column("AcceptedOn", "Accepted", 80),
             new Column("DaysWaiting", "Days waiting", 80)], [("AsAtDate", ParameterType.Date)]);

        // Agreements & title readiness.
        yield return Listing("H42", "Agreement Register", "Housing", "Agreements by type with party, stand, version in force, signature and price.", "HousingAgreements",
            [new Column("Agreement", "Agreement", 90), new Column("Type", "Type", 100), new Column("Party", "Party", 150), new Column("Stand", "Stand", 70), new Column("Version", "In force", 60),
             new Column("Status", "Status", 120), new Column("Price", "Price", 90, true)], []);
        yield return Listing("H49", "Fully Paid / Ready for Transfer", "Housing", "Sold stands whose holders owe nothing and have no title application — the most actionable list.", "HousingReadyForTitle",
            [new Column("Stand", "Stand", 80), new Column("Holder", "Holder", 170), new Column("Status", "Stand status", 90), new Column("SoldOn", "Sold", 80), new Column("Price", "Price", 90, true)], []);

        yield return Listing("H54", "Development Covenant Compliance", "Housing", "Covenants with the latest milestone, deadline, extensions and breach status.", "HousingCovenants",
            [new Column("Covenant", "Covenant", 90), new Column("Stand", "Stand", 80), new Column("Holder", "Holder", 150), new Column("Deadline", "Deadline", 80),
             new Column("Milestone", "Latest milestone", 120), new Column("Extensions", "Ext.", 40), new Column("Status", "Status", 90), new Column("DaysLeft", "Days left", 70)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H55", "Repossession Register", "Housing", "Stands repossessed with the receivable derecognised, NRV, inventory recognised and gain or loss.", "HousingRepossessions",
            [new Column("Reference", "Reference", 90), new Column("Stand", "Stand", 80), new Column("Date", "Date", 80), new Column("Reason", "Reason", 160), new Column("Receivable", "Receivable", 90, true),
             new Column("Nrv", "NRV", 90, true), new Column("Inventory", "Inventory", 90, true), new Column("GainOrLoss", "Gain / (loss)", 90, true)], []);

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

        // Council rental housing stock.
        yield return Listing("H71", "Rent Roll", "Housing", "Let units with tenant, rent and arrears.", "HousingRentRoll",
            [new Column("Unit", "Unit", 80), new Column("Type", "Type", 80), new Column("Tenancy", "Tenancy", 90), new Column("Tenant", "Tenant", 160), new Column("Since", "Since", 80),
             new Column("Rent", "Rent", 80, true), new Column("Arrears", "Arrears", 90, true)], []);
        yield return Listing("H72", "Housing Stock Register & Condition", "Housing", "Every unit with type, rooms, occupancy and its latest inspection grade.", "HousingStock",
            [new Column("Unit", "Unit", 80), new Column("Description", "Description", 160), new Column("Type", "Type", 80), new Column("Rooms", "Rooms", 50), new Column("Occupancy", "Occupancy", 80),
             new Column("Inspected", "Last inspected", 90), new Column("Grade", "Grade", 50)], []);
        yield return Listing("H73", "Occupancy & Voids Report", "Housing", "Void units with days void and rent lost to voids.", "HousingVoids",
            [new Column("Unit", "Unit", 80), new Column("Type", "Type", 80), new Column("VacantSince", "Void since", 80), new Column("DaysVoid", "Days void", 70), new Column("RentLost", "Rent lost", 90, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H76", "Eviction Pipeline & Register", "Housing", "Eviction cases with the current stage, its date and the arrears at opening.", "HousingEvictions",
            [new Column("Case", "Case", 90), new Column("Tenancy", "Tenancy", 90), new Column("Stage", "Stage", 110), new Column("StageDate", "Since", 80), new Column("Status", "Status", 90),
             new Column("Arrears", "Arrears at opening", 110, true)], []);
        yield return Listing("H77", "Lease Expiry & Renewal Due", "Housing", "Stand leases and tenancies ending within a year of the date.", "HousingLeaseExpiry",
            [new Column("Kind", "Kind", 70), new Column("Reference", "Reference", 100), new Column("Subject", "Stand / unit", 90), new Column("Ends", "Ends", 80), new Column("DaysLeft", "Days left", 70)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H80", "Inspections Due & Findings Summary", "Housing", "Units never inspected or not inspected for a year, and follow-ups outstanding.", "HousingInspectionsDue",
            [new Column("Unit", "Unit", 80), new Column("LastInspected", "Last inspected", 90), new Column("Grade", "Grade", 50), new Column("Due", "Why due", 300)], [("AsAtDate", ParameterType.Date)]);

        // Schemes, partners, cooperatives & settlements.
        yield return Listing("H85", "Developer / Joint Venture Reconciliation", "Housing", "Stands contracted vs delivered per quota, overdue milestones and penalties.", "HousingDevelopers",
            [new Column("Agreement", "Agreement", 90), new Column("Quota", "Quota", 80), new Column("Contracted", "Contracted", 70), new Column("HandedOver", "Delivered", 70), new Column("Outstanding", "Outstanding", 75),
             new Column("OverdueMilestones", "Overdue milestones", 200), new Column("PenaltyAccrued", "Penalty accrued", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H86", "Housing Cooperative Register", "Housing", "Cooperatives with active members, stands held and stands sub-allocated.", "HousingCooperatives",
            [new Column("Cooperative", "Code", 80), new Column("Name", "Name", 180), new Column("Registration", "Registration", 100), new Column("Members", "Members", 60),
             new Column("Stands", "Stands", 60), new Column("SubAllocated", "Sub-allocated", 80)], []);
        yield return Listing("H87", "Informal Settlement / Enumeration Register", "Housing", "Settlements with ward, cut-off date, households counted and eligible.", "HousingSettlements",
            [new Column("Settlement", "Code", 80), new Column("Name", "Name", 160), new Column("Ward", "Ward", 80), new Column("CutOff", "Cut-off", 80), new Column("Households", "Households", 70),
             new Column("Eligible", "Eligible", 60), new Column("Plan", "Upgrading plan", 100)], []);
        yield return Listing("H88", "Regularisation Progress Report", "Housing", "Per settlement: enumerated → eligible → waiting-listed or regularised → outstanding.", "HousingRegularisation",
            [new Column("Settlement", "Settlement", 90), new Column("Enumerated", "Enumerated", 70), new Column("Eligible", "Eligible", 60), new Column("WaitingListed", "Waiting list", 70),
             new Column("Regularised", "Regularised", 70), new Column("Outstanding", "Outstanding", 70)], []);
        yield return Listing("H89", "Housing Subsidy & Grant Utilisation", "Housing", "Grants received, spent, recognised and deferred (IAS 20), with conditions.", "HousingGrants",
            [new Column("Grant", "Grant", 90), new Column("Grantor", "Grantor", 140), new Column("Received", "Received", 80), new Column("Conditions", "Conditions assured", 100),
             new Column("Amount", "Amount", 90, true), new Column("Spent", "Spent", 90, true), new Column("Recognised", "Recognised", 90, true), new Column("Deferred", "Deferred", 90, true)], []);
    }
}
