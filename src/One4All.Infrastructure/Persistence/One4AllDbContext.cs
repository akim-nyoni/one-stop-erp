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

    /// <summary>Purchase orders.</summary>
    public DbSet<Core.Procurement.PurchaseOrder> PurchaseOrders => Set<Core.Procurement.PurchaseOrder>();

    /// <summary>Goods-received notes.</summary>
    public DbSet<Core.Procurement.GoodsReceivedNote> GoodsReceivedNotes => Set<Core.Procurement.GoodsReceivedNote>();

    /// <summary>POS terminals.</summary>
    public DbSet<Core.Pos.PosTerminal> PosTerminals => Set<Core.Pos.PosTerminal>();

    /// <summary>Till sessions.</summary>
    public DbSet<Core.Pos.TillSession> TillSessions => Set<Core.Pos.TillSession>();

    /// <summary>POS sales.</summary>
    public DbSet<Core.Pos.Sale> Sales => Set<Core.Pos.Sale>();

    /// <summary>Gift cards.</summary>
    public DbSet<Core.Vouchers.GiftCard> GiftCards => Set<Core.Vouchers.GiftCard>();

    /// <summary>Sales orders.</summary>
    public DbSet<Core.SalesOrders.SalesOrder> SalesOrders => Set<Core.SalesOrders.SalesOrder>();

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

    /// <summary>Construction contracts / jobs.</summary>
    public DbSet<Core.JobCosting.JobContract> JobContracts => Set<Core.JobCosting.JobContract>();

    /// <summary>Interim payment certificates.</summary>
    public DbSet<Core.JobCosting.ProgressCertificate> ProgressCertificates => Set<Core.JobCosting.ProgressCertificate>();

    /// <summary>Weighbridge tickets.</summary>
    public DbSet<Core.JobCosting.WeighbridgeTicket> WeighbridgeTickets => Set<Core.JobCosting.WeighbridgeTicket>();

    /// <summary>Fixed assets.</summary>
    public DbSet<Core.FixedAssets.FixedAsset> FixedAssets => Set<Core.FixedAssets.FixedAsset>();

    /// <summary>CRM opportunities.</summary>
    public DbSet<Core.Crm.Opportunity> Opportunities => Set<Core.Crm.Opportunity>();

    /// <summary>Employees.</summary>
    public DbSet<Core.Payroll.Employee> Employees => Set<Core.Payroll.Employee>();

    /// <summary>Pay elements.</summary>
    public DbSet<Core.Payroll.PayElement> PayElements => Set<Core.Payroll.PayElement>();

    /// <summary>Payslips.</summary>
    public DbSet<Core.Payroll.Payslip> Payslips => Set<Core.Payroll.Payslip>();

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
