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
        yield return Listing("H5", "Compulsory Acquisition Compensation Register", "Housing", "Compulsory acquisitions: notice served, objections, assessed, offered, paid and outstanding, and possession.",
            "HousingCompensation",
            [new Column("Claim", "Claim", 80), new Column("Parcel", "Parcel", 75), new Column("Claimant", "Claimant", 130), new Column("Lodged", "Lodged", 70), new Column("Notice", "Notice", 70),
             new Column("Objections", "Objections", 80), new Column("Status", "Status", 70), new Column("Assessed", "Assessed", 80, true), new Column("Offered", "Offered", 80, true),
             new Column("Paid", "Paid", 80, true), new Column("Outstanding", "Outstanding", 85, true), new Column("Possession", "Possession", 75)], []);
        yield return Listing("H6", "Land Disposals & Exchanges Register", "Housing", "Land sold, exchanged, donated or expropriated: resolution, area, cost out, consideration and gain or loss.",
            "HousingLandDisposals", [new Column("Disposal", "Disposal", 80), new Column("Date", "Date", 75), new Column("Parcel", "Parcel", 80), new Column("Type", "Type", 85),
             new Column("Counterparty", "Counterparty", 150), new Column("Resolution", "Resolution", 90), new Column("Area", "Area m²", 75), new Column("Cost", "Cost out", 90),
             new Column("Consideration", "Consideration", 90), new Column("Received", "Parcel received", 90), new Column("GainOrLoss", "Gain / (loss)", 90, true)], []);
        yield return Listing("H7", "Encumbrances, Servitudes & Caveats on Land", "Housing", "Every right or restriction registered against land, in force or lifted.", "HousingEncumbrances",
            [new Column("Parcel", "Parcel", 80), new Column("Type", "Type", 110), new Column("Reference", "Reference", 90), new Column("InFavourOf", "In favour of", 150),
             new Column("Registered", "Registered", 75), new Column("Status", "Status", 110), new Column("Description", "Particulars", 220)], []);
        yield return Listing("H9", "Land Bank Valuation vs Carrying Value (IAS 2 NRV test)", "Housing",
            "Undivided parcels against their latest valuation and unsold stands against their approved price, less costs to sell — the write-down working paper.",
            "HousingNrvTest", [new Column("Item", "Item", 90), new Column("Kind", "Kind", 55), new Column("Scheme", "Scheme", 80), new Column("Carrying", "Carrying", 90), new Column("Basis", "Price basis", 140),
             new Column("Price", "Estimated price", 90), new Column("CostsToSell", "Costs to sell", 85), new Column("Nrv", "NRV", 90), new Column("WriteDown", "Write-down", 90, true)],
            [("AsAtDate", ParameterType.Date), ("CostToSellPercent", ParameterType.Decimal)]);
        yield return Listing("H8", "Disputed & Encroached Land", "Housing", "Disputes open at the date with their age and current legal stage.", "HousingLandDisputes",
            [new Column("Dispute", "Dispute", 80), new Column("Parcel", "Parcel", 80), new Column("Stand", "Stand", 70), new Column("Type", "Type", 90), new Column("Claimant", "Claimant", 140),
             new Column("Opened", "Opened", 75), new Column("Stage", "Stage", 90), new Column("StageSince", "Since", 75), new Column("AgeDays", "Age (days)", 65)], [("AsAtDate", ParameterType.Date)]);

        // Planning, survey & servicing.
        yield return Listing("H10", "Layout Plans Register & Approval Status", "Housing", "Layouts with the version in force, pending amendments and approval turnaround.", "HousingLayouts",
            [new Column("Plan", "Layout", 90), new Column("Parcel", "Parcel", 90), new Column("Version", "Version", 60), new Column("Status", "Status", 110),
             new Column("Stands", "Stands", 60), new Column("TurnaroundDays", "Turnaround days", 90)], []);
        yield return Listing("H11", "Stand Creation / Subdivision Report", "Housing", "Stands created from each parent parcel, with size, scheme, status and carried cost.", "HousingSubdivision",
            [new Column("Parcel", "Parent parcel", 100), new Column("Stand", "Stand", 80), new Column("SizeM2", "Size m²", 70), new Column("Scheme", "Scheme", 100), new Column("Status", "Status", 80),
             new Column("Cost", "Cost", 90, true)], []);
        yield return Listing("H12", "Survey Status Report", "Housing", "Per scheme as at the date: stands not pegged, pegged, awaiting the Surveyor-General and approved, with the longest wait.",
            "HousingSurveyStatus", [new Column("Scheme", "Scheme", 90), new Column("Stands", "Stands", 55), new Column("NotPegged", "Not pegged", 65), new Column("Pegged", "Pegged", 55),
             new Column("AwaitingSg", "Awaiting SG", 70), new Column("Approved", "Approved", 60), new Column("LongestWait", "Longest wait (days)", 90)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H13", "Servicing Status by Scheme", "Housing", "Water, sewer, roads, electricity and storm water % complete, with stands serviced and unserviced.",
            "HousingServicingStatus", [new Column("Scheme", "Scheme", 90), new Column("Water", "Water", 55), new Column("Sewer", "Sewer", 55), new Column("Roads", "Roads", 55),
             new Column("Electricity", "Electricity", 65), new Column("StormWater", "Storm water", 70), new Column("Stands", "Stands", 55), new Column("Serviced", "Serviced", 60),
             new Column("Unserviced", "Unserviced", 65)], [("AsAtDate", ParameterType.Date)]);
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
        yield return Listing("H17", "Stand Inventory Movement & Reconciliation", "Housing", "Year to date: opening stands + created + repossessed − sold = closing, in units and cost, reconciled to the stands control account.",
            "HousingStandMovement", [new Column("Line", "Line", 200), new Column("Units", "Units", 60), new Column("Value", "Cost", 110)],
            [("AsAtDate", ParameterType.Date), ("StandsAccountCode", ParameterType.String)]);
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
        yield return Listing("H34", "Skipped Applicant / Substitution Report", "Housing", "Per allocation run: listed applicants left out and why, and offers declined, lapsed or cancelled — the appeal-defence report.",
            "HousingSkippedApplicants", [new Column("Run", "Run", 80), new Column("Scheme", "Scheme", 80), new Column("Method", "Method", 70), new Column("WaitingListNumber", "WL number", 80),
             new Column("Applicant", "Applicant", 150), new Column("Outcome", "Outcome", 90), new Column("Reason", "Reason", 240)], []);
        yield return Listing("H37", "Quota Compliance Report", "Housing", "Per scheme: allocations to each beneficiary group against its policy quota.", "HousingQuotaCompliance",
            [new Column("Scheme", "Scheme", 80), new Column("Group", "Group", 100), new Column("Allocations", "Allocations", 70), new Column("Members", "To group", 60), new Column("Actual", "Actual %", 60),
             new Column("Target", "Target %", 60), new Column("Variance", "Variance", 60), new Column("Status", "Status", 60)], []);
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
        yield return Listing("H44", "Instalment Amortisation Schedule", "Housing", "Each instalment's capital, interest and balance — for one agreement or all.", "HousingAmortisation",
            [new Column("Agreement", "Agreement", 90), new Column("Number", "No.", 40), new Column("Due", "Due", 80), new Column("Interest", "Interest", 80), new Column("Principal", "Capital", 80),
             new Column("Balance", "Balance", 90), new Column("State", "State", 70), new Column("Amount", "Instalment", 90, true)], [("AgreementNumber", ParameterType.String)]);
        yield return Listing("H45", "Housing Debtors Age Analysis", "Housing", "Instalment debtors aged 30/60/90/120+ by tenure type and scheme, as at any date.", "HousingDebtorsAgeing",
            [new Column("Tenure", "Tenure", 110), new Column("Scheme", "Scheme", 100), new Column("Debtor", "Debtor", 160), new Column("Current", "Current", 75), new Column("D30", "1-30", 70), new Column("D60", "31-60", 70),
             new Column("D90", "61-90", 70), new Column("D120", "91-120", 70), new Column("Over120", "120+", 70), new Column("Total", "Total", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H46", "Instalment Arrears & Default Report", "Housing", "Active instalment sales with overdue instalments, amount owing and last payment.", "HousingInstalmentArrears",
            [new Column("Agreement", "Agreement", 90), new Column("Stand", "Stand", 70), new Column("Buyer", "Buyer", 150), new Column("Overdue", "Overdue inst.", 80), new Column("LastPaid", "Last paid", 80),
             new Column("Owing", "Owing", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H50", "Early Settlement & Rebate Report", "Housing", "Instalment sales settled early with the interest rebated.", "HousingEarlySettlements",
            [new Column("Agreement", "Agreement", 90), new Column("Stand", "Stand", 70), new Column("Buyer", "Buyer", 150), new Column("SettledOn", "Settled", 80), new Column("Rebated", "Instalments rebated", 110),
             new Column("InterestRebated", "Interest rebated", 100, true)], []);
        yield return Listing("H51", "Deposits Held Register", "Housing", "Liability listing as at the date: purchase deposits against accepted offers and tenancy deposits, by scheme and beneficiary.",
            "HousingDepositsHeld", [new Column("Scheme", "Scheme / type", 90), new Column("Beneficiary", "Beneficiary", 150), new Column("Type", "Deposit", 100), new Column("Reference", "Reference", 90),
             new Column("Received", "Received", 75), new Column("Amount", "Amount", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H53", "Cancellations & Refund Computation", "Housing", "Agreements cancelled at the buyer's request: paid, less occupation rental, admin charge and arrears, the refund.",
            "HousingCancellations", [new Column("Agreement", "Agreement", 85), new Column("Buyer", "Buyer", 140), new Column("Date", "Cancelled", 75), new Column("Paid", "Paid", 85),
             new Column("Months", "Months", 50), new Column("Rental", "Occupation rental", 90), new Column("Admin", "Admin charge", 85), new Column("Arrears", "Arrears", 85),
             new Column("Repossession", "Repossession", 80), new Column("Refund", "Refund", 90, true)], []);
        yield return Listing("H52", "Expected Credit Loss (IFRS 9) Provision Working", "Housing", "Housing debtors' exposure and provision by portfolio and arrears band, with the allowance booked.", "HousingEcl",
            [new Column("Portfolio", "Portfolio", 140), new Column("Band", "Arrears band", 110), new Column("Exposure", "Exposure", 100), new Column("Provision", "Provision", 100, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H47", "Payment Arrangements Register", "Housing", "Arrangements with housing debtors and tenants: terms, status and standing as at the date.",
            "HousingArrangements", [new Column("Scheme", "Scheme / type", 90), new Column("Debtor", "Debtor", 150), new Column("Started", "Started", 75), new Column("Total", "Total", 85),
             new Column("Instalment", "Instalment", 80), new Column("Status", "Status", 70), new Column("DueToDate", "Due to date", 85), new Column("Paid", "Paid", 85), new Column("Missed", "Missed", 50),
             new Column("NextDue", "Next due", 75), new Column("Shortfall", "Shortfall", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H48", "Collections Report", "Housing", "Receipts from housing debtors and tenants in the month, by scheme or tenancy, day, method, currency and officer.",
            "HousingCollections", [new Column("Scheme", "Scheme / type", 90), new Column("Day", "Day", 75), new Column("Method", "Method", 85), new Column("Currency", "Currency", 55),
             new Column("Officer", "Officer", 90), new Column("Receipts", "Receipts", 55), new Column("Amount", "Amount", 95, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H49","Fully Paid / Ready for Transfer", "Housing", "Sold stands whose holders owe nothing and have no title application — the most actionable list.", "HousingReadyForTitle",
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
        yield return Listing("H61", "Deceased Estate Transfers Register", "Housing", "Inheritance and deceased-joint-holder transfers with the estate documents relied on.", "HousingEstateTransfers",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 70), new Column("Type", "Type", 120), new Column("Deceased", "From (estate of)", 150), new Column("Beneficiary", "To", 150),
             new Column("Status", "Status", 80), new Column("Documents", "Estate documents", 140)], []);
        yield return Listing("H62", "Development Levy & Transfer Fee Collection", "Housing", "Levies and fees raised, by levy, with the event they were raised on.", "HousingLevies",
            [new Column("Levy", "Levy", 90), new Column("Event", "Raised on", 110), new Column("Stand", "Stand", 70), new Column("Customer", "Charged to", 150), new Column("ChargedOn", "Date", 80),
             new Column("Invoice", "Invoice", 100), new Column("Amount", "Amount", 90, true)], []);
        yield return Listing("H63", "Transfer Duty Deducted and Remitted", "Housing", "Duty collected as agent on each transfer, and whether it has been paid over.",
            "HousingTransferDuty", [new Column("Application", "Application", 90), new Column("Stand", "Stand", 70), new Column("Consideration", "Consideration", 95), new Column("Rate", "Rate %", 55),
             new Column("Withheld", "Received", 75), new Column("Receipt", "Receipt", 90), new Column("Remitted", "Remitted", 75), new Column("Acknowledgement", "Acknowledgement", 100),
             new Column("Amount", "Duty", 90, true)], []);
        yield return Listing("H64","Title Deed Pipeline Report", "Housing", "Open title applications with stage, days at the stage, days since applied and conveyancer.", "HousingTitlePipeline",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Stage", "Stage", 110), new Column("DaysAtStage", "Days at stage", 80),
             new Column("DaysSinceApplied", "Since applied", 80), new Column("Conveyancer", "Conveyancer", 160)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H65", "Title Deeds Held but Uncollected", "Housing", "Deeds in council custody not yet handed over, aged, with the owner's contact — the chase list.", "HousingDeedsUncollected",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 80), new Column("Deed", "Deed", 100), new Column("Owner", "Owner", 160), new Column("Contact", "Contact", 160),
             new Column("DaysHeld", "Days held", 70)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H66", "Deeds Register", "Housing", "Registered deeds with registration date and current custody.", "HousingDeeds",
            [new Column("Deed", "Deed", 110), new Column("Stand", "Stand", 80), new Column("Registered", "Registered", 80), new Column("Custody", "Custody", 220)], []);
        yield return Listing("H67", "Conveyancer Performance", "Housing", "Files allocated, registered, average days to registration and registry queries per conveyancer.", "HousingConveyancers",
            [new Column("Conveyancer", "Conveyancer", 180), new Column("Files", "Files", 60), new Column("Registered", "Registered", 70), new Column("AverageDays", "Avg days to register", 120),
             new Column("Queries", "Queries raised", 90)], []);
        yield return Listing("H68","Caveats, Blocks & Disputes Register", "Housing", "Every block placed: reason, particulars, document, who placed and lifted it, age.", "HousingBlocks",
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
        yield return Listing("H75", "Rent Arrears Age Analysis", "Housing", "Tenants' arrears aged 30/60/90/120+ by unit type, as at any date.", "HousingRentAgeing",
            [new Column("Type", "Unit type", 80), new Column("Tenant", "Tenant", 160), new Column("Current", "Current", 75), new Column("D30", "1-30", 70), new Column("D60", "31-60", 70),
             new Column("D90", "61-90", 70), new Column("D120", "91-120", 70), new Column("Over120", "120+", 70), new Column("Total", "Total", 90, true)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H76","Eviction Pipeline & Register", "Housing", "Eviction cases with the current stage, its date and the arrears at opening.", "HousingEvictions",
            [new Column("Case", "Case", 90), new Column("Tenancy", "Tenancy", 90), new Column("Stage", "Stage", 110), new Column("StageDate", "Since", 80), new Column("Status", "Status", 90),
             new Column("Arrears", "Arrears at opening", 110, true)], []);
        yield return Listing("H77", "Lease Expiry & Renewal Due", "Housing", "Stand leases and tenancies ending within a year of the date.", "HousingLeaseExpiry",
            [new Column("Kind", "Kind", 70), new Column("Reference", "Reference", 100), new Column("Subject", "Stand / unit", 90), new Column("Ends", "Ends", 80), new Column("DaysLeft", "Days left", 70)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H78", "Tenant Deposits Held", "Housing", "Deposits held on active tenancies (a liability listing).", "HousingTenantDeposits",
            [new Column("Tenancy", "Tenancy", 90), new Column("Unit", "Unit", 80), new Column("Tenant", "Tenant", 170), new Column("Since", "Since", 80), new Column("Deposit", "Deposit", 90, true)], []);
        yield return Listing("H79", "Maintenance & Repairs Cost per Unit", "Housing", "Completed repairs per unit, separating what was recharged to tenants.", "HousingRepairCosts",
            [new Column("Unit", "Unit", 80), new Column("Repairs", "Repairs", 60), new Column("Recharged", "Recharged to tenants", 120), new Column("Council", "Council's cost", 100), new Column("Cost", "Total cost", 90, true)], []);
        yield return Listing("H80","Inspections Due & Findings Summary", "Housing", "Units never inspected or not inspected for a year, and follow-ups outstanding.", "HousingInspectionsDue",
            [new Column("Unit", "Unit", 80), new Column("LastInspected", "Last inspected", 90), new Column("Grade", "Grade", 50), new Column("Due", "Why due", 300)], [("AsAtDate", ParameterType.Date)]);

        yield return Listing("H81", "Straight-lined Rental Income Schedule (IFRS 16)", "Housing", "Each stand lease's contractual rent per year against the straight-line income recognised.", "HousingLeaseSchedule",
            [new Column("Lease", "Lease", 90), new Column("Year", "Year", 50), new Column("Contractual", "Contractual rent", 110), new Column("StraightLine", "Straight-line income", 120),
             new Column("Difference", "Accrued / (released)", 120, true)], []);

        // Schemes, partners, cooperatives & settlements.
        yield return Listing("H84", "Scheme Budget vs Actual", "Housing", "Per scheme and cost element (land, servicing, professional fees, overheads): budget, actual, variance and share used.",
            "HousingSchemeBudget", [new Column("Scheme", "Scheme", 90), new Column("Element", "Element", 120), new Column("Budget", "Budget", 100), new Column("Actual", "Actual", 100),
             new Column("Used", "Used %", 60), new Column("Variance", "Variance", 100, true)], []);
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

        // Control, audit & executive.
        yield return Listing("H90", "Housing Audit Trail Report", "Housing", "Every recorded change to housing records — before and after values, user, time.", "HousingAuditTrail",
            [new Column("When", "When", 120), new Column("User", "User", 90), new Column("Entity", "Record", 130), new Column("Id", "Id", 50), new Column("Action", "Action", 60), new Column("Changed", "Fields changed", 260)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H91", "Allocation & Ownership Change Approval Audit", "Housing", "Who requested and who approved each run, transfer, seniority change, price rule and block lift — turnaround and dual-authorisation exceptions.",
            "HousingApprovalAudit", [new Column("Area", "Area", 100), new Column("Reference", "Reference", 90), new Column("RequestedBy", "Requested by", 90), new Column("ApprovedBy", "Approved by", 110),
             new Column("Requested", "Requested", 75), new Column("Approved", "Approved", 75), new Column("Days", "Days", 45), new Column("Exception", "Exception", 170), new Column("Value", "Value", 90, true)], []);
        yield return Listing("H93", "Manual Override & Exception Report", "Housing", "Seniority dates changed, stand-specific prices, offers without an approved price and blocks lifted — each with the stated reason.",
            "HousingOverrides", [new Column("Type", "Type", 150), new Column("Reference", "Reference", 100), new Column("Detail", "Detail", 200), new Column("Date", "Date", 75), new Column("By", "By", 90),
             new Column("Reason", "Reason", 220)], []);
        yield return Listing("H92", "Segregation of Duties Violations — Housing", "Housing", "Users whose permissions combine housing duties that must be separated.", "HousingSodViolations",
            [new Column("User", "User", 120), new Column("Rule", "Rule", 90), new Column("Conflict", "Conflict", 360)], []);
        yield return Listing("H94", "Housing Revenue Summary", "Housing", "Year to date against the same period last year: stand sales, instalment interest, rentals and recharges, development levies.",
            "HousingRevenueSummary", [new Column("Stream", "Stream", 180), new Column("PriorYear", "Same period last year", 120), new Column("Change", "Change", 80), new Column("YearToDate", "Year to date", 110, true)],
            [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H95", "Housing Executive Dashboard", "Housing", "Land bank, stands, waiting list, allocations, titles and debtors at a glance.", "HousingDashboard",
            [new Column("Measure", "Measure", 260), new Column("Value", "Value", 140)], [("AsAtDate", ParameterType.Date)]);

        yield return Listing("H97", "Ward Housing Pack", "Housing", "One ward for its councillor: schemes, stands by status, allocations, beneficiaries, instalment arrears and the council-wide waiting list.",
            "HousingWardPack", [new Column("Section", "Section", 100), new Column("Item", "Item", 200), new Column("Value", "Value", 160)], [("Ward", ParameterType.String), ("AsAtDate", ParameterType.Date)]);

        // Applicant verification, statements and scheme delivery.
        yield return Listing("H21", "Applicant Detail / Verification Sheet", "Housing", "One applicant: every captured detail, notices, date changes and the score derivation — printable for the applicant.",
            "HousingApplicantSheet", [new Column("Section", "Section", 90), new Column("Item", "Item", 170), new Column("Value", "Value", 300)],
            [("WaitingListNumber", ParameterType.String), ("AsAtDate", ParameterType.Date)]);
        yield return Listing("H23", "Duplicate & Suspected Fraudulent Applicants", "Housing", "Fuzzy name, ID and date-of-birth matches across the waiting list, stand holders and the property register.",
            "HousingDuplicates", [new Column("FirstSource", "Source", 90), new Column("First", "Reference", 80), new Column("FirstName", "Name", 140), new Column("SecondSource", "Source", 90),
             new Column("Second", "Reference", 80), new Column("SecondName", "Name", 140), new Column("Reason", "Why", 260)], []);
        yield return Listing("H25", "Registration Renewal Compliance", "Housing", "Applicants by status with last confirmation, months since and the notices sent.", "HousingRenewals",
            [new Column("Status", "Status", 70), new Column("WaitingListNumber", "WL number", 80), new Column("Applicant", "Applicant", 150), new Column("LastConfirmed", "Last confirmed", 85),
             new Column("MonthsSince", "Months since", 60), new Column("Notices", "Notices", 50), new Column("LastNotice", "Last notice", 150)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H27", "Registration Fee Collection Report", "Housing", "Waiting-list fees received, and applicants registered without paying the registration fee.",
            "HousingRegistrationFees", [new Column("Date", "Date", 75), new Column("WaitingListNumber", "WL number", 80), new Column("Applicant", "Applicant", 150), new Column("Type", "Fee", 120),
             new Column("Receipt", "Receipt", 90), new Column("Amount", "Amount", 90, true)], []);
        yield return Listing("H28", "New Registrations & Removals Movement", "Housing", "Month by month for the year to date: opening list, registered, removed, allocated, closing.",
            "HousingRegistrationMovement", [new Column("Month", "Month", 70), new Column("Opening", "Opening", 60), new Column("Registered", "Registered", 70), new Column("Removed", "Removed", 60),
             new Column("Allocated", "Allocated", 60), new Column("Closing", "Closing", 60)], [("AsAtDate", ParameterType.Date)]);
        yield return Listing("H29", "Demand vs Supply (the Housing Gap)", "Housing", "Active applicants by category against stands and rental units available for it.", "HousingDemandSupply",
            [new Column("Category", "Category", 110), new Column("Demand", "Applicants", 70), new Column("Supply", "Available", 70), new Column("Gap", "Gap", 70)], []);
        yield return Listing("H43", "Beneficiary Account Statement", "Housing", "Charges, instalments, receipts and balance per beneficiary, with the next instalment — one or all (burst by beneficiary).",
            "HousingBeneficiaryStatement", [new Column("Beneficiary", "Beneficiary", 140), new Column("Date", "Date", 75), new Column("Document", "Document", 100), new Column("Type", "Type", 90),
             new Column("Due", "Due", 75), new Column("Balance", "Balance", 90), new Column("Amount", "Amount", 90, true)], [("Beneficiary", ParameterType.String)]);
        yield return Listing("H60", "Transfer Checklist Exception Report", "Housing", "Open transfer applications and the mandatory documents still missing.", "HousingTransferExceptions",
            [new Column("Application", "Application", 90), new Column("Stand", "Stand", 70), new Column("Type", "Type", 90), new Column("Status", "Status", 70), new Column("Lodged", "Lodged", 75),
             new Column("Missing", "Missing", 300)], []);
        yield return Listing("H74", "Tenant Statement", "Housing", "Rent charges, receipts and balance per tenant — one or all (burst by tenant).", "HousingTenantStatement",
            [new Column("Tenant", "Tenant", 140), new Column("Date", "Date", 75), new Column("Document", "Document", 100), new Column("Type", "Type", 90), new Column("Due", "Due", 75),
             new Column("Balance", "Balance", 90), new Column("Amount", "Amount", 90, true)], [("Tenant", ParameterType.String)]);
        yield return Listing("H82", "Scheme Delivery Dashboard", "Housing", "Per scheme: target, stands created, serviced, allocated, sold and titled, and % delivered.", "HousingSchemeDelivery",
            [new Column("Scheme", "Scheme", 80), new Column("Target", "Target", 60), new Column("Stands", "Stands", 60), new Column("Serviced", "Serviced", 60), new Column("Allocated", "Allocated", 60),
             new Column("Sold", "Sold", 60), new Column("Titled", "Titled", 60), new Column("Delivered", "% delivered", 70)], []);
        yield return Listing("H83", "Scheme Profitability & Cost Recovery", "Housing", "Per scheme: cost in (land and servicing), revenue from stands sold, cost recovered and margin.",
            "HousingSchemeProfitability", [new Column("Scheme", "Scheme", 80), new Column("Stands", "Stands", 55), new Column("Sold", "Sold", 55), new Column("Cost", "Cost in", 95),
             new Column("Revenue", "Revenue", 95), new Column("Recovered", "Recovered", 70), new Column("CostPerStand", "Cost / stand", 85), new Column("Margin", "Margin on sold", 95, true)], []);
        yield return Listing("H96", "Housing Delivery / Statutory Return", "Housing", "Stands delivered in the year by density, tenure and beneficiary category.", "HousingDeliveryReturn",
            [new Column("Density", "Density", 100), new Column("Tenure", "Tenure", 110), new Column("Category", "Beneficiary category", 130), new Column("Stands", "Stands", 60)],
            [("AsAtDate", ParameterType.Date)]);
    }
}
