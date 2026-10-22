using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Common;
using One4All.Core.Licensing;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence.Converters;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core write-model context. Entity configurations are discovered from
/// this assembly. Cross-cutting conventions required by the brief are applied centrally here:
/// <list type="bullet">
///   <item><description>money amounts as <c>numeric(19,4)</c>, the default for <see cref="decimal"/>;</description></item>
///   <item><description><see cref="DateTimeOffset"/> stored as <c>timestamptz</c> (Npgsql default);</description></item>
///   <item><description>optimistic concurrency via PostgreSQL's system <c>xmin</c> column.</description></item>
/// </list>
/// Individual properties that need finer scale (rates and unit prices at <c>numeric(19,6)</c>,
/// quantities at <c>numeric(19,4)</c>) override the default in their entity configuration.
/// </summary>
public class One4AllDbContext : DbContext
{
    /// <summary>Creates the context with the given options.</summary>
    /// <param name="options">EF Core options, typically configured to use Npgsql.</param>
    public One4AllDbContext(DbContextOptions<One4AllDbContext> options)
        : this(options, null)
    {
    }

    /// <summary>Creates a context limited to the signed-in user's row scope, when the host holds one.</summary>
    /// <param name="options">The options.</param>
    /// <param name="rowScope">The host's row scope (null: none).</param>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public One4AllDbContext(DbContextOptions<One4AllDbContext> options, RowScopeHolder? rowScope)
        : base(options)
    {
        if (rowScope?.Scope is Application.Security.RowScope scope)
        {
            ApplyRowScope(scope);
        }
    }

    /// <summary>The name of the query filter hiding soft-deleted rows.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    /// <summary>The name of the query filter limiting company-scoped rows to the scoped company (brief §2: row-level filtering in the DbContext).</summary>
    public const string CompanyFilter = "Company";

    /// <summary>
    /// Gets or sets the company this context is limited to: while set, every company-scoped entity reads only that company's rows (a second
    /// line of defence behind each service's own company check). Unset, nothing is filtered by company - background jobs, the desktop
    /// running as several companies, and tests.
    /// </summary>
    public long? CompanyScope { get; set; }

    /// <summary>Lifts the company scope until the returned handle is disposed, for the few services that work across companies by design.</summary>
    /// <returns>A handle restoring the scope.</returns>
    public IDisposable WithoutCompanyScope()
    {
        long? previous = CompanyScope;
        CompanyScope = null;
        return new ScopeRestorer(this, previous);
    }

    private sealed class ScopeRestorer(One4AllDbContext db, long? previous) : IDisposable
    {
        public void Dispose() => db.CompanyScope = previous;
    }

    /// <summary>Gets the branches rows are limited to (null: any).</summary>
    public long[]? ScopedBranchIds { get; private set; }

    /// <summary>Gets the warehouses rows are limited to (null: any).</summary>
    public long[]? ScopedWarehouseIds { get; private set; }

    /// <summary>Gets the tills rows are limited to (null: any).</summary>
    public long[]? ScopedTillIds { get; private set; }

    /// <summary>Gets the jobs / contracts rows are limited to (null: any).</summary>
    public long[]? ScopedJobIds { get; private set; }

    /// <summary>Gets the cost centre codes (upper case) rows are limited to (null: any).</summary>
    public string[]? ScopedCostCentres { get; private set; }

    /// <summary>Gets the department codes (upper case) rows are limited to (null: any).</summary>
    public string[]? ScopedDepartments { get; private set; }

    /// <summary>Gets the ward / zone codes (upper case) rows are limited to (null: any).</summary>
    public string[]? ScopedWards { get; private set; }

    /// <summary>
    /// Limits rows to a user's scope rules (brief §5.1): an entity holding a branch, warehouse, till, job, cost centre, department or ward
    /// reads only the rows whose value is allowed, or that have none. The API applies it to reads; writes are held to the same rules by the
    /// request checks.
    /// </summary>
    /// <param name="scope">The row scope.</param>
    public void ApplyRowScope(Application.Security.RowScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        (ScopedBranchIds, ScopedWarehouseIds, ScopedTillIds, ScopedJobIds) = (scope.BranchIds, scope.WarehouseIds, scope.TillIds, scope.JobIds);
        (ScopedCostCentres, ScopedDepartments, ScopedWards) = (scope.CostCentres, scope.Departments, scope.Wards);
    }

    // The row-scope filters: a column name, the context's list for it, and the filter's name.
    private static readonly (string Column, string Scope, string Filter)[] RowScopeColumns =
    [
        ("BranchId", nameof(ScopedBranchIds), "Scope.Branch"), ("WarehouseId", nameof(ScopedWarehouseIds), "Scope.Warehouse"), ("TerminalId", nameof(ScopedTillIds), "Scope.Till"),
        ("JobContractId", nameof(ScopedJobIds), "Scope.Job"), ("CostCentre", nameof(ScopedCostCentres), "Scope.CostCentre"), ("Department", nameof(ScopedDepartments), "Scope.Department"),
        ("Ward", nameof(ScopedWards), "Scope.Ward"), ("Zone", nameof(ScopedWards), "Scope.Zone"),
    ];

    /// <summary>Companies (sets of books).</summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>Branches / sites.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>Currency master.</summary>
    public DbSet<Currency> Currencies => Set<Currency>();

    /// <summary>Exchange rates.</summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <summary>Fiscal years.</summary>
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();

    /// <summary>Number sequences.</summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <summary>Users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the API clients (integration credentials).</summary>
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    /// <summary>Row changes caught by triggers on sensitive tables (read only).</summary>
    public DbSet<Core.Audit.DatabaseChange> DatabaseChanges => Set<Core.Audit.DatabaseChange>();

    /// <summary>Earlier password hashes, so they are not reused.</summary>
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();

    /// <summary>Gets the sign-in attempts and sessions.</summary>
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();

    /// <summary>Gets the desktop screens opened and run.</summary>
    public DbSet<ScreenUse> ScreenUses => Set<ScreenUse>();

    /// <summary>Gets the application log (<c>sys.app_log</c>).</summary>
    public DbSet<Core.System.AppLogEntry> AppLog => Set<Core.System.AppLogEntry>();

    /// <summary>Roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Groups.</summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>Permission catalogue.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>The append-only audit change log.</summary>
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    /// <summary>Gets or sets the audit chain heads written in the current transaction (see <see cref="Auditing.AuditChainLock.HeldHead"/>).</summary>
    internal (object Transaction, Dictionary<long, string> Heads)? ChainHeads { get; set; }

    /// <summary>Archived years of the audit log.</summary>
    public DbSet<AuditArchive> AuditArchives => Set<AuditArchive>();

    /// <summary>Installed licence tokens (active + history).</summary>
    public DbSet<InstalledLicence> InstalledLicences => Set<InstalledLicence>();

    /// <summary>The offline sync outbox (operations captured while a terminal was offline).</summary>
    public DbSet<Core.Offline.OfflineOperation> OfflineOperations => Set<Core.Offline.OfflineOperation>();

    /// <summary>Physical stocktake sessions.</summary>
    public DbSet<Core.Inventory.StocktakeSession> Stocktakes => Set<Core.Inventory.StocktakeSession>();

    /// <summary>Selling price lists (quantity breaks, customer-linked).</summary>
    public DbSet<Core.Inventory.PriceList> PriceLists => Set<Core.Inventory.PriceList>();

    /// <summary>Budget commitments (encumbrances).</summary>
    public DbSet<Core.Budgeting.BudgetCommitment> BudgetCommitments => Set<Core.Budgeting.BudgetCommitment>();

    /// <summary>Chart of accounts.</summary>
    public DbSet<LedgerAccount> Accounts => Set<LedgerAccount>();

    /// <summary>Cost centres.</summary>
    public DbSet<CostCentre> CostCentres => Set<CostCentre>();

    /// <summary>Gets the funds (votes).</summary>
    public DbSet<Fund> Funds => Set<Fund>();

    /// <summary>Gets the projects.</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Gets the reporting hierarchies.</summary>
    public DbSet<ReportingTree> ReportingTrees => Set<ReportingTree>();

    /// <summary>Departments (GL analysis dimension).</summary>
    public DbSet<Department> Departments => Set<Department>();

    /// <summary>Posted and draft journals.</summary>
    public DbSet<JournalEntry> Journals => Set<JournalEntry>();

    /// <summary>GL period balances.</summary>
    public DbSet<GlPeriodBalance> PeriodBalances => Set<GlPeriodBalance>();

    /// <summary>Recurring-journal templates.</summary>
    public DbSet<RecurringJournal> RecurringJournals => Set<RecurringJournal>();

    /// <summary>Gets the prepayments released to expense month by month.</summary>
    public DbSet<PrepaymentSchedule> PrepaymentSchedules => Set<PrepaymentSchedule>();

    /// <summary>Gets the VAT returns settled and filed.</summary>
    public DbSet<VatReturnFiling> VatReturnFilings => Set<VatReturnFiling>();

    /// <summary>Gets the receipt layouts (the company's and any till's own).</summary>
    public DbSet<Core.Pos.ReceiptLayout> ReceiptLayouts => Set<Core.Pos.ReceiptLayout>();

    /// <summary>Tax codes.</summary>
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();

    /// <summary>Gets the tax exemption certificates.</summary>
    public DbSet<TaxExemptionCertificate> TaxExemptionCertificates => Set<TaxExemptionCertificate>();

    /// <summary>Gets the companies' terminology overrides.</summary>
    public DbSet<TerminologyOverride> TerminologyOverrides => Set<TerminologyOverride>();

    /// <summary>Stored report definitions.</summary>
    public DbSet<Core.Reporting.ReportDefinitionRecord> ReportDefinitions => Set<Core.Reporting.ReportDefinitionRecord>();

    /// <summary>Reports featured for a company (its verticals' report set).</summary>
    public DbSet<Core.Reporting.FeaturedReport> FeaturedReports => Set<Core.Reporting.FeaturedReport>();

    /// <summary>Item categories.</summary>
    public DbSet<Core.Inventory.ItemCategory> ItemCategories => Set<Core.Inventory.ItemCategory>();

    /// <summary>Customers (Sales Ledger).</summary>
    public DbSet<Core.Trading.Customer> Customers => Set<Core.Trading.Customer>();

    /// <summary>Suppliers (Purchase Ledger).</summary>
    public DbSet<Core.Trading.Supplier> Suppliers => Set<Core.Trading.Supplier>();

    /// <summary>Trader open items (AR + AP).</summary>
    public DbSet<Core.Trading.OpenItem> OpenItems => Set<Core.Trading.OpenItem>();

    /// <summary>Customer and supplier addresses.</summary>
    public DbSet<Core.Trading.TraderAddress> TraderAddresses => Set<Core.Trading.TraderAddress>();

    /// <summary>Customer and supplier contacts.</summary>
    public DbSet<Core.Trading.TraderContact> TraderContacts => Set<Core.Trading.TraderContact>();

    /// <summary>Idempotency keys sent with posting requests.</summary>
    public DbSet<Core.System.IdempotencyRecord> IdempotencyRecords => Set<Core.System.IdempotencyRecord>();

    /// <summary>Trader allocations.</summary>
    public DbSet<Core.Trading.Allocation> Allocations => Set<Core.Trading.Allocation>();

    /// <summary>Recurring sales-invoice templates.</summary>
    public DbSet<Core.Trading.RecurringSalesInvoice> RecurringSalesInvoices => Set<Core.Trading.RecurringSalesInvoice>();

    /// <summary>Recurring purchase-invoice templates.</summary>
    public DbSet<Core.Trading.RecurringPurchaseInvoice> RecurringPurchaseInvoices => Set<Core.Trading.RecurringPurchaseInvoice>();

    /// <summary>Bank/cash accounts.</summary>
    public DbSet<Core.Banking.BankAccount> BankAccounts => Set<Core.Banking.BankAccount>();

    /// <summary>Gets the cheque register.</summary>
    public DbSet<Core.Banking.Cheque> Cheques => Set<Core.Banking.Cheque>();

    /// <summary>Bank payment-file layouts.</summary>
    public DbSet<Core.Banking.BankFileLayout> BankFileLayouts => Set<Core.Banking.BankFileLayout>();

    /// <summary>Gets the captured supplier statements.</summary>
    public DbSet<Core.Banking.SupplierStatement> SupplierStatements => Set<Core.Banking.SupplierStatement>();

    /// <summary>Imported bank statement lines.</summary>
    public DbSet<Core.Banking.BankStatementLine> BankStatementLines => Set<Core.Banking.BankStatementLine>();

    /// <summary>Workflow definitions.</summary>
    public DbSet<Core.Workflow.WorkflowDefinition> WorkflowDefinitions => Set<Core.Workflow.WorkflowDefinition>();

    /// <summary>Workflow instances.</summary>
    public DbSet<Core.Workflow.WorkflowInstance> WorkflowInstances => Set<Core.Workflow.WorkflowInstance>();

    /// <summary>Gets the single-use approval links emailed to approvers.</summary>
    public DbSet<Core.Workflow.WorkflowActionToken> WorkflowActionTokens => Set<Core.Workflow.WorkflowActionToken>();

    /// <summary>Gets the master-data changes requested for approval.</summary>
    public DbSet<Core.System.MasterDataChange> MasterDataChanges => Set<Core.System.MasterDataChange>();

    /// <summary>Inventory items.</summary>
    public DbSet<Core.Inventory.Item> Items => Set<Core.Inventory.Item>();

    /// <summary>Warehouses.</summary>
    public DbSet<Core.Inventory.Warehouse> Warehouses => Set<Core.Inventory.Warehouse>();

    /// <summary>Stock balances (item × warehouse).</summary>
    public DbSet<Core.Inventory.StockBalance> StockBalances => Set<Core.Inventory.StockBalance>();

    /// <summary>Approvers added to workflow instances (by hand or escalation).</summary>
    public DbSet<Core.Workflow.WorkflowInstanceApprover> WorkflowInstanceApprovers => Set<Core.Workflow.WorkflowInstanceApprover>();

    /// <summary>Approval delegations while approvers are away.</summary>
    public DbSet<Core.Workflow.ApprovalDelegation> ApprovalDelegations => Set<Core.Workflow.ApprovalDelegation>();

    /// <summary>Remarks on documents under approval (the comment thread).</summary>
    public DbSet<Core.Workflow.WorkflowComment> WorkflowComments => Set<Core.Workflow.WorkflowComment>();

    /// <summary>Budget enforcement rules for overruns at the spending gates.</summary>
    public DbSet<Core.Budgeting.BudgetEnforcementRule> BudgetEnforcementRules => Set<Core.Budgeting.BudgetEnforcementRule>();

    /// <summary>Scheduled deliveries of purchase order lines.</summary>
    public DbSet<Core.Procurement.PurchaseOrderDelivery> PurchaseOrderDeliveries => Set<Core.Procurement.PurchaseOrderDelivery>();

    /// <summary>Contract work breakdown structure elements.</summary>
    public DbSet<Core.JobCosting.JobWbsElement> JobWbsElements => Set<Core.JobCosting.JobWbsElement>();

    /// <summary>Bin locations.</summary>
    public DbSet<Core.Inventory.WarehouseBin> WarehouseBins => Set<Core.Inventory.WarehouseBin>();

    /// <summary>Stock held per bin.</summary>
    public DbSet<Core.Inventory.BinStock> BinStocks => Set<Core.Inventory.BinStock>();

    /// <summary>Per-warehouse reorder levels.</summary>
    public DbSet<Core.Inventory.ItemWarehouseLevel> ItemWarehouseLevels => Set<Core.Inventory.ItemWarehouseLevel>();

    /// <summary>Extra barcodes and synonyms for items.</summary>
    public DbSet<Core.Inventory.ItemAlias> ItemAliases => Set<Core.Inventory.ItemAlias>();

    /// <summary>Pack units with conversion factors.</summary>
    public DbSet<Core.Inventory.ItemUnit> ItemUnits => Set<Core.Inventory.ItemUnit>();

    /// <summary>Item–supplier links.</summary>
    public DbSet<Core.Inventory.ItemSupplier> ItemSuppliers => Set<Core.Inventory.ItemSupplier>();

    /// <summary>Stock movements.</summary>
    public DbSet<Core.Inventory.StockMovement> StockMovements => Set<Core.Inventory.StockMovement>();

    /// <summary>Gets the batch / serial quantities behind stock movements.</summary>
    public DbSet<Core.Inventory.StockLotMovement> StockLotMovements => Set<Core.Inventory.StockLotMovement>();

    /// <summary>Gets the stock adjustment reason codes.</summary>
    public DbSet<Core.Inventory.StockReasonCode> StockReasonCodes => Set<Core.Inventory.StockReasonCode>();

    /// <summary>Gets the two-step inter-warehouse transfers.</summary>
    public DbSet<Core.Inventory.StockTransfer> StockTransfers => Set<Core.Inventory.StockTransfer>();

    /// <summary>Gets the bills of materials.</summary>
    public DbSet<Core.Inventory.BillOfMaterials> BillsOfMaterials => Set<Core.Inventory.BillOfMaterials>();

    /// <summary>Gets the assembly runs.</summary>
    public DbSet<Core.Inventory.AssemblyRun> AssemblyRuns => Set<Core.Inventory.AssemblyRun>();

    /// <summary>Purchase orders.</summary>
    public DbSet<Core.Procurement.PurchaseOrder> PurchaseOrders => Set<Core.Procurement.PurchaseOrder>();

    /// <summary>Gets the framework agreements.</summary>
    public DbSet<Core.Procurement.FrameworkAgreement> FrameworkAgreements => Set<Core.Procurement.FrameworkAgreement>();

    /// <summary>Purchase requisitions.</summary>
    public DbSet<Core.Procurement.PurchaseRequisition> PurchaseRequisitions => Set<Core.Procurement.PurchaseRequisition>();

    /// <summary>Goods-received notes.</summary>
    public DbSet<Core.Procurement.GoodsReceivedNote> GoodsReceivedNotes => Set<Core.Procurement.GoodsReceivedNote>();

    /// <summary>POS terminals.</summary>
    public DbSet<Core.Pos.PosTerminal> PosTerminals => Set<Core.Pos.PosTerminal>();

    /// <summary>Gets the items pinned to the tills' favourites.</summary>
    public DbSet<Core.Pos.PosFavourite> PosFavourites => Set<Core.Pos.PosFavourite>();

    /// <summary>Gets the exchange-rate type each document type or ledger uses.</summary>
    public DbSet<Core.System.ExchangeRatePolicy> ExchangeRatePolicies => Set<Core.System.ExchangeRatePolicy>();

    /// <summary>Till sessions.</summary>
    public DbSet<Core.Pos.TillSession> TillSessions => Set<Core.Pos.TillSession>();

    /// <summary>POS sales.</summary>
    public DbSet<Core.Pos.Sale> Sales => Set<Core.Pos.Sale>();

    /// <summary>Sales parked to be resumed.</summary>
    public DbSet<Core.Pos.ParkedSale> ParkedSales => Set<Core.Pos.ParkedSale>();

    /// <summary>Till cash pickups, paid-outs and float top-ups.</summary>
    public DbSet<Core.Pos.TillCashMovement> TillCashMovements => Set<Core.Pos.TillCashMovement>();

    /// <summary>The loyalty scheme.</summary>
    public DbSet<Core.Pos.LoyaltyScheme> LoyaltySchemes => Set<Core.Pos.LoyaltyScheme>();

    /// <summary>Loyalty members.</summary>
    public DbSet<Core.Pos.LoyaltyMember> LoyaltyMembers => Set<Core.Pos.LoyaltyMember>();

    /// <summary>Gets the stand surrender requests awaiting a second user's approval.</summary>
    public DbSet<Core.Housing.SurrenderRequest> StandSurrenderRequests => Set<Core.Housing.SurrenderRequest>();

    /// <summary>Loyalty points movements.</summary>
    public DbSet<Core.Pos.LoyaltyMovement> LoyaltyMovements => Set<Core.Pos.LoyaltyMovement>();

    /// <summary>POS promotions.</summary>
    public DbSet<Core.Pos.Promotion> Promotions => Set<Core.Pos.Promotion>();

    /// <summary>Gets the till event log (voids, refunds, discounts, overrides, no-sales, parking, loyalty, reprints, fiscal).</summary>
    public DbSet<Core.Pos.PosEvent> PosEvents => Set<Core.Pos.PosEvent>();

    /// <summary>Gift cards.</summary>
    public DbSet<Core.Vouchers.GiftCard> GiftCards => Set<Core.Vouchers.GiftCard>();

    /// <summary>Sales orders.</summary>
    public DbSet<Core.SalesOrders.SalesOrder> SalesOrders => Set<Core.SalesOrders.SalesOrder>();

    /// <summary>Sales order versions replaced by amendments.</summary>
    public DbSet<Core.SalesOrders.SalesOrderRevision> SalesOrderRevisions => Set<Core.SalesOrders.SalesOrderRevision>();

    /// <summary>Gets the delivery notes.</summary>
    public DbSet<Core.SalesOrders.SalesDelivery> SalesDeliveries => Set<Core.SalesOrders.SalesDelivery>();

    /// <summary>Sales-order lines the supplier delivers straight to the customer.</summary>
    public DbSet<Core.SalesOrders.DropShipment> DropShipments => Set<Core.SalesOrders.DropShipment>();

    /// <summary>Customer supply contracts drawn down by call-offs.</summary>
    public DbSet<Core.SalesOrders.CustomerContract> CustomerContracts => Set<Core.SalesOrders.CustomerContract>();

    /// <summary>Delivery charge rates by zone.</summary>
    public DbSet<Core.SalesOrders.DeliveryZoneRate> DeliveryZoneRates => Set<Core.SalesOrders.DeliveryZoneRate>();

    /// <summary>Sales quotations.</summary>
    public DbSet<Core.SalesOrders.Quotation> Quotations => Set<Core.SalesOrders.Quotation>();

    /// <summary>Municipal tariffs.</summary>
    public DbSet<Core.Municipal.Tariff> Tariffs => Set<Core.Municipal.Tariff>();

    /// <summary>Gets the council's own service types.</summary>
    public DbSet<Core.Municipal.MunicipalServiceType> MunicipalServiceTypes => Set<Core.Municipal.MunicipalServiceType>();

    /// <summary>Municipal properties / stands.</summary>
    public DbSet<Core.Municipal.Property> Properties => Set<Core.Municipal.Property>();

    /// <summary>Municipal meters.</summary>
    public DbSet<Core.Municipal.Meter> Meters => Set<Core.Municipal.Meter>();

    /// <summary>Municipal meter readings.</summary>
    public DbSet<Core.Municipal.MeterReading> MeterReadings => Set<Core.Municipal.MeterReading>();

    /// <summary>Gets the bulk water supplied into each zone.</summary>
    public DbSet<Core.Municipal.BulkSupplyReading> BulkSupplyReadings => Set<Core.Municipal.BulkSupplyReading>();

    /// <summary>Housing land parcels (land bank).</summary>
    public DbSet<Core.Housing.LandParcel> LandParcels => Set<Core.Housing.LandParcel>();

    /// <summary>Housing schemes.</summary>
    public DbSet<Core.Housing.HousingScheme> HousingSchemes => Set<Core.Housing.HousingScheme>();

    /// <summary>Housing stands.</summary>
    public DbSet<Core.Housing.HousingStand> HousingStands => Set<Core.Housing.HousingStand>();

    /// <summary>Gets the refused second allocations (the housing exception report's record of blocked attempts).</summary>
    public DbSet<Core.Housing.AllocationBlockLog> AllocationBlockLogs => Set<Core.Housing.AllocationBlockLog>();

    /// <summary>Housing applicants (waiting list).</summary>
    public DbSet<Core.Housing.HousingApplicant> HousingApplicants => Set<Core.Housing.HousingApplicant>();

    /// <summary>School fee structures.</summary>
    public DbSet<Core.Schools.FeeStructure> FeeStructures => Set<Core.Schools.FeeStructure>();

    /// <summary>Students.</summary>
    public DbSet<Core.Schools.Student> Students => Set<Core.Schools.Student>();

    /// <summary>Gets the academic years.</summary>
    public DbSet<Core.Schools.AcademicYear> AcademicYears => Set<Core.Schools.AcademicYear>();

    /// <summary>Gets the terms.</summary>
    public DbSet<Core.Schools.SchoolTerm> SchoolTerms => Set<Core.Schools.SchoolTerm>();

    /// <summary>Gets the classes.</summary>
    public DbSet<Core.Schools.SchoolClass> SchoolClasses => Set<Core.Schools.SchoolClass>();

    /// <summary>Bursary requests and their decisions.</summary>
    public DbSet<Core.Schools.BursaryAward> BursaryAwards => Set<Core.Schools.BursaryAward>();

    /// <summary>Gets the school transport routes.</summary>
    public DbSet<Core.Schools.TransportRoute> TransportRoutes => Set<Core.Schools.TransportRoute>();

    /// <summary>Gets the hostels.</summary>
    public DbSet<Core.Schools.Hostel> Hostels => Set<Core.Schools.Hostel>();

    /// <summary>Gets the student wallet movements.</summary>
    public DbSet<Core.Schools.StudentWalletEntry> StudentWalletEntries => Set<Core.Schools.StudentWalletEntry>();

    /// <summary>Construction contracts / jobs.</summary>
    public DbSet<Core.JobCosting.JobContract> JobContracts => Set<Core.JobCosting.JobContract>();

    /// <summary>Gets the contract variation orders.</summary>
    public DbSet<Core.JobCosting.VariationOrder> VariationOrders => Set<Core.JobCosting.VariationOrder>();

    /// <summary>Gets the bill-of-quantities lines.</summary>
    public DbSet<Core.JobCosting.BoqItem> BoqItems => Set<Core.JobCosting.BoqItem>();

    /// <summary>Gets the job timesheets.</summary>
    public DbSet<Core.JobCosting.JobTimesheet> JobTimesheets => Set<Core.JobCosting.JobTimesheet>();

    /// <summary>Gets the subcontracts.</summary>
    public DbSet<Core.JobCosting.Subcontract> Subcontracts => Set<Core.JobCosting.Subcontract>();

    /// <summary>Gets the plant register.</summary>
    public DbSet<Core.JobCosting.PlantItem> PlantItems => Set<Core.JobCosting.PlantItem>();

    /// <summary>Gets the plant hours and running costs.</summary>
    public DbSet<Core.JobCosting.PlantLog> PlantLogs => Set<Core.JobCosting.PlantLog>();

    /// <summary>Interim payment certificates.</summary>
    public DbSet<Core.JobCosting.ProgressCertificate> ProgressCertificates => Set<Core.JobCosting.ProgressCertificate>();

    /// <summary>Weighbridge tickets.</summary>
    public DbSet<Core.JobCosting.WeighbridgeTicket> WeighbridgeTickets => Set<Core.JobCosting.WeighbridgeTicket>();

    /// <summary>Gets the weighbridge vehicle register.</summary>
    public DbSet<Core.JobCosting.WeighbridgeVehicle> WeighbridgeVehicles => Set<Core.JobCosting.WeighbridgeVehicle>();

    /// <summary>Gets the haulage charging zones.</summary>
    public DbSet<Core.JobCosting.HaulageZone> HaulageZones => Set<Core.JobCosting.HaulageZone>();

    /// <summary>Fixed assets.</summary>
    public DbSet<Core.FixedAssets.FixedAsset> FixedAssets => Set<Core.FixedAssets.FixedAsset>();

    /// <summary>Fixed-asset maintenance log entries.</summary>
    public DbSet<Core.FixedAssets.AssetMaintenance> AssetMaintenance => Set<Core.FixedAssets.AssetMaintenance>();

    /// <summary>Gets the physical asset verifications.</summary>
    public DbSet<Core.FixedAssets.AssetCount> AssetCounts => Set<Core.FixedAssets.AssetCount>();

    /// <summary>Usage readings of units-of-production assets.</summary>
    public DbSet<Core.FixedAssets.AssetUsageReading> AssetUsageReadings => Set<Core.FixedAssets.AssetUsageReading>();

    /// <summary>Gets the planned maintenance tasks.</summary>
    public DbSet<Core.FixedAssets.MaintenancePlan> MaintenancePlans => Set<Core.FixedAssets.MaintenancePlan>();

    /// <summary>Gets the capital projects (CWIP).</summary>
    public DbSet<Core.FixedAssets.CapitalProject> CapitalProjects => Set<Core.FixedAssets.CapitalProject>();

    /// <summary>Fixed-asset disposal records.</summary>
    public DbSet<Core.FixedAssets.AssetDisposal> AssetDisposals => Set<Core.FixedAssets.AssetDisposal>();

    /// <summary>CRM opportunities.</summary>
    public DbSet<Core.Crm.Opportunity> Opportunities => Set<Core.Crm.Opportunity>();

    /// <summary>Employees.</summary>
    public DbSet<Core.Payroll.Employee> Employees => Set<Core.Payroll.Employee>();

    /// <summary>Pay elements.</summary>
    public DbSet<Core.Payroll.PayElement> PayElements => Set<Core.Payroll.PayElement>();

    /// <summary>Gets the approved establishment.</summary>
    public DbSet<Core.Payroll.EstablishmentPost> EstablishmentPosts => Set<Core.Payroll.EstablishmentPost>();

    /// <summary>Gets the salary scales (grade and notch).</summary>
    public DbSet<Core.Payroll.SalaryScale> SalaryScales => Set<Core.Payroll.SalaryScale>();

    /// <summary>Gets the acting appointments.</summary>
    public DbSet<Core.Payroll.ActingAppointment> ActingAppointments => Set<Core.Payroll.ActingAppointment>();

    /// <summary>Gets the documents on employee files.</summary>
    public DbSet<Core.Payroll.EmployeeDocument> EmployeeDocuments => Set<Core.Payroll.EmployeeDocument>();

    /// <summary>Gets the disciplinary records.</summary>
    public DbSet<Core.Payroll.DisciplinaryRecord> DisciplinaryRecords => Set<Core.Payroll.DisciplinaryRecord>();

    /// <summary>Gets the training and qualification records.</summary>
    public DbSet<Core.Payroll.TrainingRecord> TrainingRecords => Set<Core.Payroll.TrainingRecord>();

    /// <summary>Gets the appraisals.</summary>
    public DbSet<Core.Payroll.EmployeeAppraisal> EmployeeAppraisals => Set<Core.Payroll.EmployeeAppraisal>();

    /// <summary>Gets the garnishee / court orders.</summary>
    public DbSet<Core.Payroll.GarnisheeOrder> GarnisheeOrders => Set<Core.Payroll.GarnisheeOrder>();

    /// <summary>Gets the dated leave records.</summary>
    public DbSet<Core.Payroll.LeaveRecord> LeaveRecords => Set<Core.Payroll.LeaveRecord>();

    /// <summary>Gets the daily attendance.</summary>
    public DbSet<Core.Payroll.AttendanceRecord> AttendanceRecords => Set<Core.Payroll.AttendanceRecord>();

    /// <summary>Gets the split-pay instructions.</summary>
    public DbSet<Core.Payroll.EmployeePaymentSplit> PaymentSplits => Set<Core.Payroll.EmployeePaymentSplit>();

    /// <summary>Gets the payslip print log.</summary>
    public DbSet<Core.Payroll.PayslipPrint> PayslipPrints => Set<Core.Payroll.PayslipPrint>();

    /// <summary>Legacy payroll lines imported for the parallel run.</summary>
    public DbSet<Core.Payroll.LegacyPayslipLine> LegacyPayslipLines => Set<Core.Payroll.LegacyPayslipLine>();

    /// <summary>Deductions suspended by net-pay protection and arrears recovered.</summary>
    public DbSet<Core.Payroll.DeductionArrear> DeductionArrears => Set<Core.Payroll.DeductionArrear>();

    /// <summary>Leave applications made through self-service.</summary>
    public DbSet<Core.Payroll.LeaveApplication> LeaveApplications => Set<Core.Payroll.LeaveApplication>();

    /// <summary>Detail changes asked for through self-service.</summary>
    public DbSet<Core.Payroll.DetailChangeRequest> DetailChangeRequests => Set<Core.Payroll.DetailChangeRequest>();

    /// <summary>AP invoices raised for payroll third parties.</summary>
    public DbSet<Core.Payroll.RemittanceInvoice> RemittanceInvoices => Set<Core.Payroll.RemittanceInvoice>();

    /// <summary>Variable input for pay periods.</summary>
    public DbSet<Core.Payroll.PayrollVariable> PayrollVariables => Set<Core.Payroll.PayrollVariable>();

    /// <summary>Payroll runs calculated and awaiting approval.</summary>
    public DbSet<Core.Payroll.PayrollDraft> PayrollDrafts => Set<Core.Payroll.PayrollDraft>();

    /// <summary>Backpay calculation lines.</summary>
    public DbSet<Core.Payroll.BackpayLine> BackpayLines => Set<Core.Payroll.BackpayLine>();

    /// <summary>Payslips.</summary>
    public DbSet<Core.Payroll.Payslip> Payslips => Set<Core.Payroll.Payslip>();

    /// <summary>Employee loans.</summary>
    public DbSet<Core.Payroll.EmployeeLoan> EmployeeLoans => Set<Core.Payroll.EmployeeLoan>();

    /// <summary>Employees' benefits in kind.</summary>
    public DbSet<Core.Payroll.EmployeeBenefit> EmployeeBenefits => Set<Core.Payroll.EmployeeBenefit>();

    /// <summary>The motor vehicle benefit scale.</summary>
    public DbSet<Core.Payroll.VehicleBenefitScale> VehicleBenefitScales => Set<Core.Payroll.VehicleBenefitScale>();

    /// <summary>Employee leave balances.</summary>
    public DbSet<Core.Payroll.EmployeeLeaveBalance> EmployeeLeaveBalances => Set<Core.Payroll.EmployeeLeaveBalance>();

    /// <summary>Employees' beneficiaries and medical-aid dependants.</summary>
    public DbSet<Core.Payroll.EmployeeDependant> EmployeeDependants => Set<Core.Payroll.EmployeeDependant>();

    /// <summary>Employee expense claims.</summary>
    public DbSet<Core.Expenses.ExpenseClaim> ExpenseClaims => Set<Core.Expenses.ExpenseClaim>();

    /// <summary>Budgets.</summary>
    public DbSet<Core.Budgeting.Budget> Budgets => Set<Core.Budgeting.Budget>();

    /// <summary>Actual volumes against budgeted accounts.</summary>
    public DbSet<Core.Budgeting.ActualVolume> ActualVolumes => Set<Core.Budgeting.ActualVolume>();

    /// <summary>Gets the planning drivers.</summary>
    public DbSet<Core.Budgeting.BudgetDriver> BudgetDrivers => Set<Core.Budgeting.BudgetDriver>();

    /// <summary>Gets the departmental ceilings.</summary>
    public DbSet<Core.Budgeting.BudgetCeiling> BudgetCeilings => Set<Core.Budgeting.BudgetCeiling>();

    /// <summary>Gets the department hierarchy and budget holders.</summary>
    public DbSet<Core.Budgeting.DepartmentUnit> DepartmentUnits => Set<Core.Budgeting.DepartmentUnit>();

    /// <summary>Gets the utilisation alerts already sent.</summary>
    public DbSet<Core.Budgeting.BudgetAlertSent> BudgetAlertsSent => Set<Core.Budgeting.BudgetAlertSent>();

    /// <summary>Cashiering stations.</summary>
    public DbSet<Core.Receipting.CashierStation> CashierStations => Set<Core.Receipting.CashierStation>();

    /// <summary>Cashier sessions.</summary>
    public DbSet<Core.Receipting.CashierSession> CashierSessions => Set<Core.Receipting.CashierSession>();

    /// <summary>Receipts.</summary>
    public DbSet<Core.Receipting.Receipt> Receipts => Set<Core.Receipting.Receipt>();

    /// <summary>Banking deposits (treasury).</summary>
    public DbSet<Core.Receipting.BankDeposit> BankDeposits => Set<Core.Receipting.BankDeposit>();

    /// <summary>Import batches (bulk/file-based receipting).</summary>
    public DbSet<Core.Receipting.ImportBatch> ImportBatches => Set<Core.Receipting.ImportBatch>();

    /// <summary>Session cash movements (pickups, petty-cash pay-outs).</summary>
    public DbSet<Core.Receipting.SessionCashMovement> SessionCashMovements => Set<Core.Receipting.SessionCashMovement>();

    /// <summary>Refunds paid to payers.</summary>
    public DbSet<Core.Receipting.RefundRecord> Refunds => Set<Core.Receipting.RefundRecord>();

    /// <summary>Receipt reprint log.</summary>
    public DbSet<Core.Receipting.ReceiptReprint> ReceiptReprints => Set<Core.Receipting.ReceiptReprint>();

    /// <summary>Payment arrangements captured at the counter.</summary>
    public DbSet<Core.Receipting.PaymentArrangement> PaymentArrangements => Set<Core.Receipting.PaymentArrangement>();

    /// <summary>Suspense allocation log.</summary>
    public DbSet<Core.Receipting.SuspenseAllocation> SuspenseAllocations => Set<Core.Receipting.SuspenseAllocation>();

    /// <summary>Credit-balance refunds asked for and awaiting (or given) a second user's approval.</summary>
    public DbSet<Core.Receipting.ReceiptRefundRequest> ReceiptRefundRequests => Set<Core.Receipting.ReceiptRefundRequest>();

    /// <summary>Dishonoured cheques / reversed payments.</summary>
    public DbSet<Core.Receipting.DishonouredPayment> DishonouredPayments => Set<Core.Receipting.DishonouredPayment>();

    /// <summary>Pending / unconfirmed tenders.</summary>
    public DbSet<Core.Receipting.PendingTender> PendingTenders => Set<Core.Receipting.PendingTender>();

    /// <summary>Segregation-of-duties violation attempts.</summary>
    public DbSet<Core.Receipting.SegregationViolation> SegregationViolations => Set<Core.Receipting.SegregationViolation>();

    /// <summary>Restaurant menu items.</summary>
    public DbSet<Core.Restaurant.MenuItem> MenuItems => Set<Core.Restaurant.MenuItem>();

    /// <summary>Restaurant tables.</summary>
    public DbSet<Core.Restaurant.RestaurantTable> RestaurantTables => Set<Core.Restaurant.RestaurantTable>();

    /// <summary>Restaurant table orders.</summary>
    public DbSet<Core.Restaurant.TableOrder> TableOrders => Set<Core.Restaurant.TableOrder>();

    /// <summary>Kitchen order tickets.</summary>
    public DbSet<Core.Restaurant.KitchenTicket> KitchenTickets => Set<Core.Restaurant.KitchenTicket>();

    /// <summary>Tip pool allocations.</summary>
    public DbSet<Core.Restaurant.TipAllocation> TipAllocations => Set<Core.Restaurant.TipAllocation>();

    /// <summary>Table reservations.</summary>
    public DbSet<Core.Restaurant.TableReservation> TableReservations => Set<Core.Restaurant.TableReservation>();

    /// <summary>Walk-in waitlist entries.</summary>
    public DbSet<Core.Restaurant.WaitlistEntry> WaitlistEntries => Set<Core.Restaurant.WaitlistEntry>();

    /// <summary>Combined table groups.</summary>
    public DbSet<Core.Restaurant.TableGroup> TableGroups => Set<Core.Restaurant.TableGroup>();

    /// <summary>Floor-plan sections.</summary>
    public DbSet<Core.Restaurant.FloorSection> FloorSections => Set<Core.Restaurant.FloorSection>();

    /// <summary>Per-company Restaurant policy (auto-gratuity).</summary>
    public DbSet<Core.Restaurant.RestaurantPolicy> RestaurantPolicies => Set<Core.Restaurant.RestaurantPolicy>();

    /// <summary>Stock wastage records.</summary>
    public DbSet<Core.Restaurant.WastageRecord> WastageRecords => Set<Core.Restaurant.WastageRecord>();

    /// <summary>Persisted restaurant bills (audit trail).</summary>
    public DbSet<Core.Restaurant.RestaurantBill> RestaurantBills => Set<Core.Restaurant.RestaurantBill>();

    /// <summary>Collection agents.</summary>
    public DbSet<Core.Receipting.Agent> Agents => Set<Core.Receipting.Agent>();

    /// <summary>Pre-numbered stationery books.</summary>
    public DbSet<Core.Receipting.StationeryBook> StationeryBooks => Set<Core.Receipting.StationeryBook>();

    /// <inheritdoc />
    /// <summary>The entities stored in partitioned tables (by year), which carry no xmin row version.</summary>
    public static readonly IReadOnlySet<Type> PartitionedEntityTypes = new HashSet<Type>
    {
        typeof(Core.Audit.ChangeLogEntry), typeof(Core.Municipal.MeterReading), typeof(Core.Accounting.JournalLine), typeof(Core.Pos.SaleLine), typeof(Core.Payroll.PayslipLine),
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map the xmin system column as the concurrency token for every entity deriving from Entity.
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Not the partitioned tables (the audit log, meter readings and the journal, sale and payslip lines): PostgreSQL cannot return xmin from an insert into a partitioned table.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType) && !PartitionedEntityTypes.Contains(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>(nameof(Entity.RowVersion))
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
        }

        // Entity type configurations (IEntityTypeConfiguration<T>) are discovered here as modules land.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(One4AllDbContext).Assembly);
        foreach (Type partitioned in PartitionedEntityTypes)
        {
            modelBuilder.Entity(partitioned).Ignore(nameof(Entity.RowVersion));
        }

        // Every company-scoped root entity: CompanyScope == null || CompanyId == CompanyScope, read from this context at query time.
        System.Linq.Expressions.MemberExpression scope = System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(this), nameof(CompanyScope));
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is not null || entityType.IsOwned() || !typeof(ICompanyScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            System.Linq.Expressions.ParameterExpression row = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "row");
            System.Linq.Expressions.Expression body = System.Linq.Expressions.Expression.OrElse(
                System.Linq.Expressions.Expression.Equal(scope, System.Linq.Expressions.Expression.Constant(null, typeof(long?))),
                System.Linq.Expressions.Expression.Equal(
                    System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(row, nameof(ICompanyScoped.CompanyId)), typeof(long?)), scope));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(CompanyFilter, System.Linq.Expressions.Expression.Lambda(body, row));
        }

        // Scope rules: list == null || column == null (or "") || list.Contains(column) - codes compared in upper case.
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is not null || entityType.IsOwned())
            {
                continue;
            }

            foreach ((string column, string scopeName, string filter) in RowScopeColumns)
            {
                Microsoft.EntityFrameworkCore.Metadata.IMutableProperty? property = entityType.FindProperty(column);
                Type? type = property?.ClrType;
                if (property?.PropertyInfo is null || (type != typeof(long) && type != typeof(long?) && type != typeof(string)))
                {
                    continue;
                }

                System.Linq.Expressions.ParameterExpression row = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "row");
                System.Linq.Expressions.MemberExpression list = System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(this), scopeName);
                System.Linq.Expressions.Expression value = System.Linq.Expressions.Expression.Property(row, property.PropertyInfo);
                Type element = list.Type.GetElementType()!;
                System.Linq.Expressions.Expression untagged;
                System.Linq.Expressions.Expression key;
                if (type == typeof(string))
                {
                    untagged = System.Linq.Expressions.Expression.OrElse(System.Linq.Expressions.Expression.Equal(value, System.Linq.Expressions.Expression.Constant(null, typeof(string))),
                        System.Linq.Expressions.Expression.Equal(value, System.Linq.Expressions.Expression.Constant(string.Empty)));
                    key = System.Linq.Expressions.Expression.Call(value, typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!);
                }
                else if (type == typeof(long?))
                {
                    untagged = System.Linq.Expressions.Expression.Equal(value, System.Linq.Expressions.Expression.Constant(null, typeof(long?)));
                    key = System.Linq.Expressions.Expression.Property(value, nameof(Nullable<long>.Value));
                }
                else
                {
                    untagged = System.Linq.Expressions.Expression.Constant(false);
                    key = value;
                }

                if (key.Type != element)
                {
                    continue; // a column of another kind under the same name
                }

                System.Linq.Expressions.Expression contains = System.Linq.Expressions.Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [element], list, key);
                System.Linq.Expressions.Expression body = System.Linq.Expressions.Expression.OrElse(
                    System.Linq.Expressions.Expression.Equal(list, System.Linq.Expressions.Expression.Constant(null, list.Type)),
                    System.Linq.Expressions.Expression.OrElse(untagged, contains));
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter, System.Linq.Expressions.Expression.Lambda(body, row));
            }
        }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Every money amount defaults to numeric(19,4). Rates/prices/quantities override per-property.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        // Bound string columns to a sane default; long text opts in explicitly.
        configurationBuilder.Properties<string>().HaveMaxLength(256);

        // Domain value objects mapped to primitive columns.
        configurationBuilder.Properties<CurrencyCode>().HaveConversion<CurrencyCodeConverter>().HaveMaxLength(3);
    }
}
