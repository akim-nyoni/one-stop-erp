using One4All.Core.Licensing;

namespace One4All.Core.Security;

/// <summary>
/// The master list of every permission in the system, grouped by module. This is the source of truth
/// (brief §18.3): it is seeded into <c>sec.permission</c> and drives the permission-grid UI. Adding a
/// feature means adding a line here - never scattering permission strings through the code.
/// </summary>
public static class PermissionCatalogue
{
    private static readonly List<PermissionDefinition> Items = Build();

    /// <summary>All permission definitions, ordered by module then feature.</summary>
    public static IReadOnlyList<PermissionDefinition> All => Items;

    /// <summary>Returns the definitions belonging to a licensing module.</summary>
    /// <param name="licenceModule">A code from <see cref="LicenceModules"/>.</param>
    /// <returns>The matching definitions.</returns>
    public static IEnumerable<PermissionDefinition> ForLicenceModule(string licenceModule) =>
        Items.Where(p => string.Equals(p.LicenceModule, licenceModule, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a definition by its <c>Module.Feature</c> code.</summary>
    /// <param name="code">The permission code.</param>
    /// <returns>The definition, or null if unknown.</returns>
    public static PermissionDefinition? Find(string code) =>
        Items.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

    private static List<PermissionDefinition> Build()
    {
        List<PermissionDefinition> list = [];

        void Add(string module, string licence, string feature, string name, OperationFlags flags) =>
            list.Add(new PermissionDefinition($"{module}.{feature}", module, feature, name, flags, licence));

        const OperationFlags master = PermissionDefinition.MasterData;
        const OperationFlags txn = PermissionDefinition.Transaction;
        const OperationFlags ro = PermissionDefinition.ReadOnly;
        const OperationFlags auth = PermissionDefinition.AllAuthoriseLevels;

        // ---- System Manager (always licensed: CORE) ----
        const string sys = "System";
        Add(sys, LicenceModules.Core, "Company", "Companies", master);
        Add(sys, LicenceModules.Core, "Branch", "Branches / Sites", master);
        Add(sys, LicenceModules.Core, "Period", "Accounting Periods", master | OperationFlags.Post);
        Add(sys, LicenceModules.Core, "Currency", "Currencies", master);
        Add(sys, LicenceModules.Core, "ExchangeRate", "Exchange Rates (override = key a rate on a document)", master | OperationFlags.BackDate | OperationFlags.OverridePrice);
        Add(sys, LicenceModules.Core, "NumberSequence", "Number Sequences", master);
        Add(sys, LicenceModules.Core, "TaxCode", "Tax Codes", master);
        Add(sys, LicenceModules.Core, "DocumentLayout", "Document Layouts", master);
        Add(sys, LicenceModules.Core, "TerminalProfile", "Terminal Profiles", master);
        Add(sys, LicenceModules.Core, "ScheduledJob", "Scheduled Jobs", master | OperationFlags.Approve);
        Add(sys, LicenceModules.Core, "Backup", "Backup / Restore", OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        Add(sys, LicenceModules.Core, "DataHealth", "Data Health Dashboard", ro);
        Add(sys, LicenceModules.Core, "Licence", "Licence Status", OperationFlags.View | OperationFlags.Create);

        // ---- Security ----
        const string sec = "Security";
        Add(sec, LicenceModules.Core, "User", "Users", master);
        Add(sec, LicenceModules.Core, "Role", "Roles", master);
        Add(sec, LicenceModules.Core, "Group", "Groups", master);
        Add(sec, LicenceModules.Core, "Permission", "Permission Assignment", OperationFlags.View | OperationFlags.Edit);
        Add(sec, LicenceModules.Core, "Scope", "Data Scopes", master);
        Add(sec, LicenceModules.Core, "SodMatrix", "Segregation-of-Duties Matrix", master);
        Add(sec, LicenceModules.Core, "AuditTrail", "Audit Trail Enquiry", ro);

        // ---- General Ledger ----
        const string gl = "GeneralLedger";
        Add(gl, LicenceModules.GeneralLedger, "Account", "Chart of Accounts", master);
        Add(gl, LicenceModules.GeneralLedger, "Journal", "Manual Journals", txn | OperationFlags.BackDate);
        Add(gl, LicenceModules.GeneralLedger, "PeriodAdjustment", "Adjustments in Closed (Soft-Closed) Periods", OperationFlags.View | OperationFlags.Post);
        Add(gl, LicenceModules.GeneralLedger, "RecurringJournal", "Recurring Journals", txn);
        Add(gl, LicenceModules.GeneralLedger, "YearEnd", "Year-End Routine", OperationFlags.View | OperationFlags.Post | OperationFlags.VoidReverse | auth);
        Add(gl, LicenceModules.GeneralLedger, "Revaluation", "FX Revaluation", OperationFlags.View | OperationFlags.Post | OperationFlags.VoidReverse);
        Add(gl, LicenceModules.GeneralLedger, "Enquiry", "Nominal Enquiry", ro);
        Add(gl, LicenceModules.GeneralLedger, "TrialBalance", "Trial Balance", ro);

        // ---- Accounts Receivable ----
        const string ar = "Sales";
        Add(ar, LicenceModules.AccountsReceivable, "Customer", "Customers", master | OperationFlags.OverrideCreditLimit);
        Add(ar, LicenceModules.AccountsReceivable, "Invoice", "Sales Invoices", txn | OperationFlags.OverridePrice | OperationFlags.OverrideDiscount);
        Add(ar, LicenceModules.AccountsReceivable, "CreditNote", "Credit Notes", txn);
        Add(ar, LicenceModules.AccountsReceivable, "Receipt", "Receipts", txn);
        Add(ar, LicenceModules.AccountsReceivable, "Allocation", "Allocation / Matching", OperationFlags.View | OperationFlags.Edit | OperationFlags.Post | OperationFlags.VoidReverse);
        Add(ar, LicenceModules.AccountsReceivable, "CreditControl", "Credit Control", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve | OperationFlags.Print);
        Add(ar, LicenceModules.AccountsReceivable, "Statement", "Customer Statements", ro | OperationFlags.Reprint);
        Add(ar, LicenceModules.AccountsReceivable, "WriteOff", "Bad Debt Write-off", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | auth);

        // ---- Accounts Payable ----
        const string ap = "Purchasing";
        Add(ap, LicenceModules.AccountsPayable, "Supplier", "Suppliers", master);
        Add(ap, LicenceModules.AccountsPayable, "SupplierBankDetail", "Supplier Bank Details", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve | auth);
        Add(ap, LicenceModules.AccountsPayable, "Invoice", "Supplier Invoices", txn);
        Add(ap, LicenceModules.AccountsPayable, "Allocation", "Allocation / Matching", OperationFlags.View | OperationFlags.Edit | OperationFlags.Post | OperationFlags.VoidReverse);
        Add(ap, LicenceModules.AccountsPayable, "PaymentRun", "Payment Runs", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve | auth);
        Add(ap, LicenceModules.AccountsPayable, "Statement", "Supplier Reconciliation", ro);

        // ---- Cash Book ----
        const string cb = "CashBook";
        Add(cb, LicenceModules.CashBook, "BankAccount", "Bank / Cash Accounts", master);
        Add(cb, LicenceModules.CashBook, "Transaction", "Receipts & Payments", txn);
        Add(cb, LicenceModules.CashBook, "Transfer", "Account Transfers", txn);
        Add(cb, LicenceModules.CashBook, "Reconciliation", "Bank Reconciliation", OperationFlags.View | OperationFlags.Edit | OperationFlags.Post | OperationFlags.Print);

        // ---- Inventory ----
        const string inv = "Inventory";
        Add(inv, LicenceModules.Inventory, "Item", "Item Master", master | OperationFlags.ViewCost | OperationFlags.ViewMargin);
        Add(inv, LicenceModules.Inventory, "Warehouse", "Warehouses / Bins", master);
        Add(inv, LicenceModules.Inventory, "GoodsReceipt", "Goods Receipt", txn);
        Add(inv, LicenceModules.Inventory, "Issue", "Stock Issue (authorise = let stock go negative)", txn | OperationFlags.AuthoriseLevel1);
        Add(inv, LicenceModules.Inventory, "Transfer", "Stock Transfer", txn);
        Add(inv, LicenceModules.Inventory, "Adjustment", "Stock Adjustment", txn | OperationFlags.AuthoriseLevel1);
        Add(inv, LicenceModules.Inventory, "StockTake", "Stock Take / Count", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Post | OperationFlags.Approve | OperationFlags.Print);
        Add(inv, LicenceModules.Inventory, "Weighbridge", "Weighbridge Tickets", txn | OperationFlags.OverridePrice);
        Add(inv, LicenceModules.Inventory, "Valuation", "Stock Valuation", ro | OperationFlags.ViewCost);

        // ---- Procurement ----
        const string proc = "Procurement";
        Add(proc, LicenceModules.Procurement, "Requisition", "Requisitions", txn | auth | OperationFlags.OverrideDiscount);
        Add(proc, LicenceModules.Procurement, "Rfq", "RFQ / Tender", txn | auth);
        Add(proc, LicenceModules.Procurement, "PurchaseOrder", "Purchase Orders", txn | auth);
        Add(proc, LicenceModules.Procurement, "Grn", "Goods Received Notes", txn | OperationFlags.AuthoriseLevel1);
        Add(proc, LicenceModules.Procurement, "InvoiceMatch", "3-Way Invoice Matching", OperationFlags.View | OperationFlags.Edit | OperationFlags.Post | OperationFlags.Approve);

        // ---- Sales Order Processing ----
        const string sop = "SalesOrders";
        Add(sop, LicenceModules.SalesOrders, "Quotation", "Quotations", txn | OperationFlags.OverridePrice);
        Add(sop, LicenceModules.SalesOrders, "Order", "Sales Orders", txn | OperationFlags.OverridePrice | OperationFlags.OverrideDiscount);
        Add(sop, LicenceModules.SalesOrders, "Despatch", "Despatch / Delivery", txn);

        // ---- Point of Sale ----
        const string pos = "Pos";
        Add(pos, LicenceModules.PointOfSale, "Sale", "Process Sale", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Print | OperationFlags.OverridePrice | OperationFlags.OverrideDiscount);
        Add(pos, LicenceModules.PointOfSale, "Refund", "Refunds / Returns", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.AuthoriseLevel1 | OperationFlags.AuthoriseLevel2);
        Add(pos, LicenceModules.PointOfSale, "Void", "Void Sale", OperationFlags.View | OperationFlags.VoidReverse | OperationFlags.AuthoriseLevel1);
        Add(pos, LicenceModules.PointOfSale, "NoSale", "No-Sale / Drawer Open", OperationFlags.View | OperationFlags.Create);
        Add(pos, LicenceModules.PointOfSale, "TillSession", "Till Sessions (X/Z Read)", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Print | OperationFlags.Reprint);
        Add(pos, LicenceModules.PointOfSale, "Promotion", "Promotions", master);
        Add(pos, LicenceModules.PointOfSale, "PriceOverride", "Supervisor Price Override", OperationFlags.OverridePrice | OperationFlags.AuthoriseLevel1);
        Add(pos, LicenceModules.PointOfSale, "ManualWeight", "Manual Weight Entry", OperationFlags.Create | OperationFlags.AuthoriseLevel1);

        // ---- Vouchers ----
        const string vou = "Voucher";
        Add(vou, LicenceModules.Vouchers, "GiftCard", "Gift Cards", txn);
        Add(vou, LicenceModules.Vouchers, "Coupon", "Discount Vouchers / Coupons", master);
        Add(vou, LicenceModules.Vouchers, "PaymentVoucher", "Payment/Receipt/Journal Vouchers", txn | auth);

        // ---- Municipality ----
        const string mun = "Municipal";
        Add(mun, LicenceModules.Municipal, "Property", "Property Register", master);
        Add(mun, LicenceModules.Municipal, "ValuationRoll", "Valuation Roll", master | OperationFlags.Approve);
        Add(mun, LicenceModules.Municipal, "Service", "Property Services", master);
        Add(mun, LicenceModules.Municipal, "Tariff", "Tariffs", master | OperationFlags.Approve | OperationFlags.BackDate);
        Add(mun, LicenceModules.Municipal, "Meter", "Meters", master);
        Add(mun, LicenceModules.Municipal, "Reading", "Meter Readings", txn | OperationFlags.BackDate);
        Add(mun, LicenceModules.Municipal, "BillingRun", "Billing Runs", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve | auth);
        Add(mun, LicenceModules.Municipal, "DebtManagement", "Debt Management", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve | OperationFlags.Print);
        Add(mun, LicenceModules.Municipal, "Clearance", "Rates Clearance Certificate", OperationFlags.View | OperationFlags.Create | OperationFlags.Print | OperationFlags.AuthoriseLevel1);
        Add(mun, LicenceModules.Municipal, "Licence", "Licences & Permits", master | OperationFlags.Approve);
        Add(mun, LicenceModules.Municipal, "ServiceTicket", "Service Desk / Faults", txn);

        AddHousing(list, master, auth);
        AddPayroll(list, master, auth, ro);
        AddBudget(list, auth, ro);

        // ---- Schools ----
        const string sch = "School";
        Add(sch, LicenceModules.School, "Student", "Students", master);
        Add(sch, LicenceModules.School, "FeeStructure", "Fee Structures", master | OperationFlags.Approve);
        Add(sch, LicenceModules.School, "Bursary", "Bursaries / Discounts", OperationFlags.View | OperationFlags.Create | OperationFlags.Approve | auth);
        Add(sch, LicenceModules.School, "BillingRun", "Fee Billing Runs", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve);
        Add(sch, LicenceModules.School, "Statement", "Fee Statements", ro | OperationFlags.Reprint);
        Add(sch, LicenceModules.School, "Restricted", "Restricted Student Records", ro);

        // ---- Job Costing / Construction ----
        const string job = "JobCosting";
        Add(job, LicenceModules.JobCosting, "Contract", "Contracts / Jobs", master);
        Add(job, LicenceModules.JobCosting, "Budget", "Job Budgets / BOQ", master | OperationFlags.Approve);
        Add(job, LicenceModules.JobCosting, "VariationOrder", "Variation Orders", txn | auth);
        Add(job, LicenceModules.JobCosting, "Certificate", "Payment Certificates", txn | auth);
        Add(job, LicenceModules.JobCosting, "Subcontractor", "Subcontractor Ledger", master | OperationFlags.Post);

        // ---- Fixed Assets ----
        const string fa = "FixedAssets";
        Add(fa, LicenceModules.FixedAssets, "Asset", "Asset Register", master);
        Add(fa, LicenceModules.FixedAssets, "Depreciation", "Depreciation Run", OperationFlags.View | OperationFlags.Post | OperationFlags.VoidReverse);
        Add(fa, LicenceModules.FixedAssets, "Disposal", "Disposals", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve);
        Add(fa, LicenceModules.FixedAssets, "Verification", "Asset Verification", OperationFlags.View | OperationFlags.Create | OperationFlags.Print);

        // ---- CRM / Service Desk ----
        const string crm = "Crm";
        Add(crm, LicenceModules.Crm, "Lead", "Leads / Opportunities", master);
        Add(crm, LicenceModules.Crm, "Ticket", "Service Tickets", txn);

        // ---- Reporting ----
        const string rpt = "Reporting";
        Add(rpt, LicenceModules.Core, "Report", "Run Reports", ro | OperationFlags.Reprint);
        Add(rpt, LicenceModules.ReportDesigner, "Designer", "Report Designer", master);
        Add(rpt, LicenceModules.Core, "Schedule", "Report Scheduling & Bursting", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Post);

        AddScreenFeatures(list, master, txn, ro, auth);

        return list
            .OrderBy(p => p.Module, StringComparer.Ordinal)
            .ThenBy(p => p.Feature, StringComparer.Ordinal)
            .ToList();
    }

    // Features the desktop's screens are gated by (Application.Security.ScreenPermissions maps every screen to one), beyond the headline ones above.
    private static void AddScreenFeatures(List<PermissionDefinition> list, OperationFlags master, OperationFlags txn, OperationFlags ro, OperationFlags auth)
    {
        void Add(string module, string licence, string feature, string name, OperationFlags flags) =>
            list.Add(new PermissionDefinition($"{module}.{feature}", module, feature, name, flags, licence));

        const OperationFlags setting = OperationFlags.View | OperationFlags.Edit;
        const OperationFlags run = OperationFlags.View | OperationFlags.Create | OperationFlags.Post;

        Add("System", LicenceModules.Core, "Workflow", "Approval Workflow (stages, delegation, escalation)", setting | OperationFlags.Approve | OperationFlags.Create);
        Add("System", LicenceModules.Core, "Industry", "Industry Setup & Sample Data", run);
        Add("System", LicenceModules.Core, "Integration", "API Keys, Webhooks & Offline Queue", master);
        Add("System", LicenceModules.Core, "Import", "Data Import & Templates", run | OperationFlags.Export);
        Add("System", LicenceModules.Core, "Printer", "Printers per Workstation", setting);
        Add("System", LicenceModules.Core, "Attachment", "Attachment Rules", setting);
        Add("System", LicenceModules.Core, "Encryption", "Payroll Encryption Keys", setting | OperationFlags.Post);

        Add("GeneralLedger", LicenceModules.GeneralLedger, "Tax", "VAT Returns & Tax Exemption Certificates", txn);
        Add("GeneralLedger", LicenceModules.GeneralLedger, "Accrual", "Accruals, Prepayments & Deferred Income", txn);

        Add("Sales", LicenceModules.AccountsReceivable, "PriceList", "Price Lists", master | OperationFlags.OverridePrice);
        Add("Sales", LicenceModules.AccountsReceivable, "Commission", "Sales Commissions", master | OperationFlags.Post);
        Add("Sales", LicenceModules.AccountsReceivable, "Layby", "Lay-bys", txn);
        Add("Sales", LicenceModules.AccountsReceivable, "Contract", "Customer Contracts & Call-offs", master | OperationFlags.Post);

        Add("Purchasing", LicenceModules.AccountsPayable, "WithholdingTax", "Withholding Tax Returns", ro | OperationFlags.Post);

        Add("CashBook", LicenceModules.CashBook, "Cheque", "Cheques", txn);
        Add("CashBook", LicenceModules.CashBook, "StandingOrder", "Standing Orders & Direct Debits", master | OperationFlags.Post);
        Add("CashBook", LicenceModules.CashBook, "Forecast", "Cash-flow Forecast", ro);
        Add("CashBook", LicenceModules.CashBook, "BankFileLayout", "Bank File Layouts", master);
        Add("CashBook", LicenceModules.CashBook, "PettyCash", "Petty Cash", txn);
        Add("CashBook", LicenceModules.CashBook, "Teller", "Teller / Cashier Till", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Print | OperationFlags.Reprint | OperationFlags.VoidReverse);
        Add("CashBook", LicenceModules.CashBook, "CashOffice", "Cash Office Setup (stations, collectors, receipt books, matching rules)", master);

        Add("Inventory", LicenceModules.Inventory, "Assembly", "Assembly & Production Batches", txn);
        Add("Inventory", LicenceModules.Inventory, "Price", "Stock Prices & Future Prices", master | OperationFlags.OverridePrice);
        Add("Inventory", LicenceModules.Inventory, "Replenishment", "Replenishment, Reorder Levels & ABC", master);
        Add("Inventory", LicenceModules.Inventory, "Consignment", "Supplier- and Customer-owned Stock", master | OperationFlags.Post);

        Add("Procurement", LicenceModules.Procurement, "SupplierPerformance", "Supplier Rebates & Scorecards", master);

        Add("Pos", LicenceModules.PointOfSale, "Loyalty", "Loyalty Programme", master | OperationFlags.Post);
        Add("Pos", LicenceModules.PointOfSale, "TillSetting", "Till Settings (categories, currency, rounding)", master);
        Add("Pos", LicenceModules.PointOfSale, "Restaurant", "Restaurant Orders, Tables & Reservations", txn);
        Add("Pos", LicenceModules.PointOfSale, "Kitchen", "Kitchen Tickets & Wastage", run | OperationFlags.Print);
        Add("Pos", LicenceModules.PointOfSale, "RestaurantSetup", "Menus, Floor Plan & Kitchen Printers", master);

        Add("Municipal", LicenceModules.Municipal, "Facility", "Cemeteries & Facility Bookings", master | OperationFlags.Post);
        Add("Municipal", LicenceModules.Municipal, "Prepaid", "Prepaid Vending", txn);

        Add("JobCosting", LicenceModules.JobCosting, "Timesheet", "Timesheets & Plant Hours", txn);

        Add("FixedAssets", LicenceModules.FixedAssets, "CapitalProject", "Capital Projects (CWIP)", txn);
        Add("FixedAssets", LicenceModules.FixedAssets, "Maintenance", "Asset Maintenance & Usage", master | OperationFlags.Post);

        Add("Crm", LicenceModules.Crm, "Activity", "Activities", txn);
        Add("Crm", LicenceModules.Crm, "WhatsApp", "WhatsApp Inbox", run);

        Add("Payroll", LicenceModules.Payroll, "Attendance", "Attendance, Rosters & Time Clock", txn);
        Add("Payroll", LicenceModules.Payroll, "SelfService", "Self-service Requests & Overtime Approval", OperationFlags.View | OperationFlags.Approve | auth);
        Add("Payroll", LicenceModules.Payroll, "Expense", "Expense Claims", txn | auth);
        Add("Payroll", LicenceModules.Payroll, "HrRecord", "HR Records (appraisals, training, discipline, documents)", master);
        Add("Payroll", LicenceModules.Payroll, "Medical", "Medical Records (restricted)", master);

        Add("School", LicenceModules.School, "AcademicYear", "Academic Years, Terms & Classes", master);
        Add("School", LicenceModules.School, "Wallet", "Student Wallets", txn);
    }

    private static void AddHousing(List<PermissionDefinition> list, OperationFlags master, OperationFlags auth)
    {
        // Housing permission grid (brief §4.10.11), each with the standard flags plus Authorise-Level-1..5.
        void H(string feature, string name, OperationFlags flags) =>
            list.Add(new PermissionDefinition($"Housing.{feature}", "Housing", feature, name, flags, LicenceModules.Housing));

        H("LandParcel", "Land Parcels", master);
        H("Acquisition", "Land Acquisition", master | auth);
        H("Subdivision", "Subdivision into Stands", master | OperationFlags.Approve);
        H("Stand", "Stand Register", master);
        H("Pricing", "Stand Pricing", master | OperationFlags.Approve | OperationFlags.OverridePrice | OperationFlags.BackDate);
        H("WaitingList", "Waiting List", master);
        H("ApplicantVerify", "Applicant Verification", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve);
        H("RegistrationDate", "Change Registration Date", OperationFlags.View | OperationFlags.Edit | auth);
        H("AllocationRun", "Allocation Run", OperationFlags.View | OperationFlags.Create | OperationFlags.Print);
        H("AllocationApprove", "Allocation Approval", OperationFlags.View | OperationFlags.Approve | auth);
        H("Ballot", "Ballot / Draw", OperationFlags.View | OperationFlags.Create | OperationFlags.Print);
        H("Offer", "Offers", txnLike());
        H("Agreement", "Agreements", master | OperationFlags.Approve | auth);
        H("InstalmentSchedule", "Instalment Schedules", OperationFlags.View | OperationFlags.Create | OperationFlags.Print);
        H("OwnershipChange", "Change of Ownership / Cession", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        H("OwnershipApprove", "Ownership Change Approval", OperationFlags.View | OperationFlags.Approve | auth);
        H("Caveat", "Caveats / Blocks", OperationFlags.View | OperationFlags.Create | OperationFlags.Delete | OperationFlags.AuthoriseLevel3);
        H("TitleDeed", "Title Deed Pipeline", master);
        H("Repossession", "Repossession", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | auth);
        H("Tenancy", "Council Rental Tenancy", master);
        H("RentRun", "Rent Debit Run", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve);
        H("Eviction", "Eviction Workflow", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | auth);
        H("Scheme", "Housing Schemes", master);
        H("Cooperative", "Housing Cooperatives", master);
        H("InformalSettlement", "Informal Settlements", master);

        static OperationFlags txnLike() =>
            OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Print | OperationFlags.Post;
    }

    private static void AddPayroll(List<PermissionDefinition> list, OperationFlags master, OperationFlags auth, OperationFlags ro)
    {
        // Payroll walled-garden permissions (brief §4.15.10).
        void P(string feature, string name, OperationFlags flags) =>
            list.Add(new PermissionDefinition($"Payroll.{feature}", "Payroll", feature, name, flags, LicenceModules.Payroll));

        P("Employee", "Employee Master", master);
        P("BankDetails", "Employee Bank Details", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve | auth);
        P("PayElement", "Pay Elements", master);
        P("TaxTable", "Tax Tables", master | OperationFlags.BackDate);
        P("SalaryChange", "Salary Changes", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve | auth);
        P("Variables", "Variable Input", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Export);
        P("Calculate", "Calculate Run", OperationFlags.View | OperationFlags.Create);
        P("Validate", "Validate Run", OperationFlags.View | OperationFlags.Create | OperationFlags.Print);
        P("Approve", "Approve Run", OperationFlags.View | OperationFlags.Approve | auth);
        P("Commit", "Commit Run", OperationFlags.View | OperationFlags.Post);
        P("Reverse", "Reverse Run", OperationFlags.View | OperationFlags.VoidReverse | auth);
        P("Payslip", "Payslips", ro | OperationFlags.Reprint);
        P("PayslipReprint", "Payslip Reprint", OperationFlags.Reprint);
        P("BankFile", "Bank EFT File", OperationFlags.View | OperationFlags.Create | OperationFlags.Export);
        P("StatutoryReturn", "Statutory Returns", ro | OperationFlags.Export);
        P("Termination", "Terminations", OperationFlags.View | OperationFlags.Create | OperationFlags.Post | OperationFlags.Approve);
        P("Loan", "Staff Loans", master | OperationFlags.Approve);
        P("Leave", "Leave", txnLike());
        P("LeaveApprove", "Leave Approval", OperationFlags.View | OperationFlags.Approve);
        P("Establishment", "Establishment / Posts", master | OperationFlags.Approve);
        P("ViewSalary", "View Salary (sensitive)", OperationFlags.View);
        P("Audit", "Payroll Audit Trail (sensitive)", OperationFlags.View);
        P("Reports", "Payroll Reports", ro | OperationFlags.Export);

        static OperationFlags txnLike() =>
            OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Print;
    }

    private static void AddBudget(List<PermissionDefinition> list, OperationFlags auth, OperationFlags ro)
    {
        // Budgeting permissions (brief §4.18.7).
        void B(string feature, string name, OperationFlags flags) =>
            list.Add(new PermissionDefinition($"Budget.{feature}", "Budget", feature, name, flags, LicenceModules.Budget));

        OperationFlags edit = OperationFlags.View | OperationFlags.Create | OperationFlags.Edit;

        B("Prepare", "Prepare Budget", edit | OperationFlags.Export);
        B("SubmitOwnDepartment", "Submit Own Department", OperationFlags.View | OperationFlags.Post);
        B("ViewOwnDepartment", "View Own Department", ro);
        B("ViewAllDepartments", "View All Departments", ro);
        B("Review", "Review / Annotate", OperationFlags.View | OperationFlags.Edit);
        B("Recommend", "Recommend", OperationFlags.View | OperationFlags.Approve);
        B("Approve", "Approve Budget", OperationFlags.View | OperationFlags.Approve | auth);
        B("Activate", "Activate Version", OperationFlags.View | OperationFlags.Post);
        B("Supersede", "Supersede Version", OperationFlags.View | OperationFlags.Post | OperationFlags.Approve);
        B("Supplementary", "Supplementary Appropriation", edit | OperationFlags.Approve | auth);
        B("Virement", "Virement Request", edit);
        B("VirementApprove", "Virement Approval", OperationFlags.View | OperationFlags.Approve | auth);
        B("Rephase", "Rephasing", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve);
        B("Reservation", "Reservations / Earmarking", edit);
        B("OverrideBlock", "Override Budget Block", OperationFlags.View | OperationFlags.OverridePrice | auth);
        B("Assumptions", "Assumptions / Drivers", edit);
        B("Target", "Set Targets / Ceilings", edit | OperationFlags.Approve);
        B("Forecast", "Reforecast", edit);
        B("Commentary", "Variance Commentary", OperationFlags.View | OperationFlags.Edit);
        B("Archive", "Archive Budgets", OperationFlags.View | OperationFlags.Post);
        B("Reports", "Budget Reports", ro | OperationFlags.Export);
    }
}
