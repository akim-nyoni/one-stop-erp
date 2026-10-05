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
        : base(options)
    {
    }

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

    /// <summary>Gets the sign-in attempts and sessions.</summary>
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();

    /// <summary>Roles.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Groups.</summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>Permission catalogue.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>The append-only audit change log.</summary>
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

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

    /// <summary>Departments (GL analysis dimension).</summary>
    public DbSet<Department> Departments => Set<Department>();

    /// <summary>Posted and draft journals.</summary>
    public DbSet<JournalEntry> Journals => Set<JournalEntry>();

    /// <summary>GL period balances.</summary>
    public DbSet<GlPeriodBalance> PeriodBalances => Set<GlPeriodBalance>();

    /// <summary>Recurring-journal templates.</summary>
    public DbSet<RecurringJournal> RecurringJournals => Set<RecurringJournal>();

    /// <summary>Tax codes.</summary>
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();

    /// <summary>Stored report definitions.</summary>
    public DbSet<Core.Reporting.ReportDefinitionRecord> ReportDefinitions => Set<Core.Reporting.ReportDefinitionRecord>();

    /// <summary>Customers (Sales Ledger).</summary>
    public DbSet<Core.Trading.Customer> Customers => Set<Core.Trading.Customer>();

    /// <summary>Suppliers (Purchase Ledger).</summary>
    public DbSet<Core.Trading.Supplier> Suppliers => Set<Core.Trading.Supplier>();

    /// <summary>Trader open items (AR + AP).</summary>
    public DbSet<Core.Trading.OpenItem> OpenItems => Set<Core.Trading.OpenItem>();

    /// <summary>Trader allocations.</summary>
    public DbSet<Core.Trading.Allocation> Allocations => Set<Core.Trading.Allocation>();

    /// <summary>Recurring sales-invoice templates.</summary>
    public DbSet<Core.Trading.RecurringSalesInvoice> RecurringSalesInvoices => Set<Core.Trading.RecurringSalesInvoice>();

    /// <summary>Recurring purchase-invoice templates.</summary>
    public DbSet<Core.Trading.RecurringPurchaseInvoice> RecurringPurchaseInvoices => Set<Core.Trading.RecurringPurchaseInvoice>();

    /// <summary>Bank/cash accounts.</summary>
    public DbSet<Core.Banking.BankAccount> BankAccounts => Set<Core.Banking.BankAccount>();

    /// <summary>Imported bank statement lines.</summary>
    public DbSet<Core.Banking.BankStatementLine> BankStatementLines => Set<Core.Banking.BankStatementLine>();

    /// <summary>Workflow definitions.</summary>
    public DbSet<Core.Workflow.WorkflowDefinition> WorkflowDefinitions => Set<Core.Workflow.WorkflowDefinition>();

    /// <summary>Workflow instances.</summary>
    public DbSet<Core.Workflow.WorkflowInstance> WorkflowInstances => Set<Core.Workflow.WorkflowInstance>();

    /// <summary>Inventory items.</summary>
    public DbSet<Core.Inventory.Item> Items => Set<Core.Inventory.Item>();

    /// <summary>Warehouses.</summary>
    public DbSet<Core.Inventory.Warehouse> Warehouses => Set<Core.Inventory.Warehouse>();

    /// <summary>Stock balances (item × warehouse).</summary>
    public DbSet<Core.Inventory.StockBalance> StockBalances => Set<Core.Inventory.StockBalance>();

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

    /// <summary>Purchase requisitions.</summary>
    public DbSet<Core.Procurement.PurchaseRequisition> PurchaseRequisitions => Set<Core.Procurement.PurchaseRequisition>();

    /// <summary>Goods-received notes.</summary>
    public DbSet<Core.Procurement.GoodsReceivedNote> GoodsReceivedNotes => Set<Core.Procurement.GoodsReceivedNote>();

    /// <summary>POS terminals.</summary>
    public DbSet<Core.Pos.PosTerminal> PosTerminals => Set<Core.Pos.PosTerminal>();

    /// <summary>Till sessions.</summary>
    public DbSet<Core.Pos.TillSession> TillSessions => Set<Core.Pos.TillSession>();

    /// <summary>POS sales.</summary>
    public DbSet<Core.Pos.Sale> Sales => Set<Core.Pos.Sale>();

    /// <summary>Gets the till event log (voids, refunds, discounts, overrides, no-sales, parking, loyalty, reprints, fiscal).</summary>
    public DbSet<Core.Pos.PosEvent> PosEvents => Set<Core.Pos.PosEvent>();

    /// <summary>Gift cards.</summary>
    public DbSet<Core.Vouchers.GiftCard> GiftCards => Set<Core.Vouchers.GiftCard>();

    /// <summary>Sales orders.</summary>
    public DbSet<Core.SalesOrders.SalesOrder> SalesOrders => Set<Core.SalesOrders.SalesOrder>();

    /// <summary>Sales quotations.</summary>
    public DbSet<Core.SalesOrders.Quotation> Quotations => Set<Core.SalesOrders.Quotation>();

    /// <summary>Municipal tariffs.</summary>
    public DbSet<Core.Municipal.Tariff> Tariffs => Set<Core.Municipal.Tariff>();

    /// <summary>Municipal properties / stands.</summary>
    public DbSet<Core.Municipal.Property> Properties => Set<Core.Municipal.Property>();

    /// <summary>Municipal meters.</summary>
    public DbSet<Core.Municipal.Meter> Meters => Set<Core.Municipal.Meter>();

    /// <summary>Municipal meter readings.</summary>
    public DbSet<Core.Municipal.MeterReading> MeterReadings => Set<Core.Municipal.MeterReading>();

    /// <summary>Housing land parcels (land bank).</summary>
    public DbSet<Core.Housing.LandParcel> LandParcels => Set<Core.Housing.LandParcel>();

    /// <summary>Housing schemes.</summary>
    public DbSet<Core.Housing.HousingScheme> HousingSchemes => Set<Core.Housing.HousingScheme>();

    /// <summary>Housing stands.</summary>
    public DbSet<Core.Housing.HousingStand> HousingStands => Set<Core.Housing.HousingStand>();

    /// <summary>Housing applicants (waiting list).</summary>
    public DbSet<Core.Housing.HousingApplicant> HousingApplicants => Set<Core.Housing.HousingApplicant>();

    /// <summary>School fee structures.</summary>
    public DbSet<Core.Schools.FeeStructure> FeeStructures => Set<Core.Schools.FeeStructure>();

    /// <summary>Students.</summary>
    public DbSet<Core.Schools.Student> Students => Set<Core.Schools.Student>();

    /// <summary>Gets the school transport routes.</summary>
    public DbSet<Core.Schools.TransportRoute> TransportRoutes => Set<Core.Schools.TransportRoute>();

    /// <summary>Gets the hostels.</summary>
    public DbSet<Core.Schools.Hostel> Hostels => Set<Core.Schools.Hostel>();

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

    /// <summary>Payslips.</summary>
    public DbSet<Core.Payroll.Payslip> Payslips => Set<Core.Payroll.Payslip>();

    /// <summary>Employee loans.</summary>
    public DbSet<Core.Payroll.EmployeeLoan> EmployeeLoans => Set<Core.Payroll.EmployeeLoan>();

    /// <summary>Employee leave balances.</summary>
    public DbSet<Core.Payroll.EmployeeLeaveBalance> EmployeeLeaveBalances => Set<Core.Payroll.EmployeeLeaveBalance>();

    /// <summary>Employee expense claims.</summary>
    public DbSet<Core.Expenses.ExpenseClaim> ExpenseClaims => Set<Core.Expenses.ExpenseClaim>();

    /// <summary>Budgets.</summary>
    public DbSet<Core.Budgeting.Budget> Budgets => Set<Core.Budgeting.Budget>();

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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map the xmin system column as the concurrency token for every entity deriving from Entity.
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
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
