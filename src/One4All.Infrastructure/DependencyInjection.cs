using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Auditing;
using One4All.Infrastructure.Persistence.RawSql;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Security;
using One4All.Licensing.Core;

namespace One4All.Infrastructure;

/// <summary>
/// Registers the infrastructure services (EF Core context + audit interceptor, security, licensing,
/// seeders) into the DI container. Both the ASP.NET Core host and the embedded desktop host call this
/// with the same connection string, so there is a single data-access code path (brief §2.2).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Adds One4All infrastructure services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="licensingOptions">The licensing options (trusted keys); defaults to empty.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOne4AllInfrastructure(
        this IServiceCollection services,
        string connectionString,
        LicensingOptions? licensingOptions = null)
    {
        // Cross-cutting.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());

        // Persistence + audit interceptor (scoped, so it sees the ambient user).
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<One4AllDbContext>((sp, options) =>
            options.UseOne4All(connectionString)
                   .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));

        // Security services.
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton(new AuthenticationOptions());
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();

        // Accounting.
        services.AddScoped<IPostingEngine, PostingEngine>();
        services.AddScoped<IRecurringJournalService, Accounting.RecurringJournalService>();
        services.AddScoped<IAccrualService, Accounting.AccrualService>();
        services.AddScoped<IPeriodService, PeriodService>();
        services.AddScoped<IYearEndService, YearEndService>();
        services.AddScoped<ITaxCalculator, TaxCalculator>();
        services.AddScoped<IFxRevaluationService, FxRevaluationService>();
        services.AddSingleton<IFiscalDeviceProvider, NullFiscalDeviceProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IGeneralLedgerReports, GeneralLedgerReports>();
        services.AddScoped<Application.Abstractions.Reporting.IFinancialReportRenderer, Reporting.FinancialReportRenderer>();
        services.AddScoped<Persistence.Seeding.ReportCatalogueSeeder>();

        // Generic report runner + live data providers (whole-system report binding).
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.GeneralLedgerReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.TradingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.ReceiptingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.VerticalReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.PosJobReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.RegistersReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportRunner, Reporting.ReportRunner>();

        // Trading sub-ledgers (AR/AP + Cash Book).
        services.AddScoped<Trading.TradingService>();
        services.AddScoped<Application.Abstractions.Trading.ISalesLedgerService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.IPurchaseLedgerService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.IAllocationService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.ITradingReports, Trading.TradingReports>();
        services.AddScoped<Application.Abstractions.Trading.IPaymentRunService, Trading.PaymentRunService>();
        services.AddScoped<Application.Abstractions.Trading.IRecurringSalesInvoiceService, Trading.RecurringSalesInvoiceService>();
        services.AddScoped<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService, Trading.RecurringPurchaseInvoiceService>();
        services.AddScoped<Application.Abstractions.Banking.IBankReconciliationService, Banking.BankReconciliationService>();

        // Generic workflow / approvals engine.
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowService, Workflow.WorkflowService>();

        // Inventory + Procurement.
        services.AddScoped<Application.Abstractions.Inventory.IInventoryService, Inventory.InventoryService>();
        services.AddScoped<Application.Abstractions.Procurement.IProcurementService, Procurement.ProcurementService>();
        services.AddScoped<Application.Abstractions.Procurement.IPurchaseRequisitionService, Procurement.PurchaseRequisitionService>();
        services.AddScoped<Application.Abstractions.Pos.IPosService, Pos.PosService>();
        services.AddScoped<Application.Abstractions.Vouchers.IVoucherService, Vouchers.VoucherService>();
        services.AddScoped<Application.Abstractions.SalesOrders.ISalesOrderService, SalesOrders.SalesOrderService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IQuotationService, SalesOrders.QuotationService>();
        services.AddScoped<Application.Abstractions.Crm.IOpportunityService, Crm.OpportunityService>();

        // Municipality / Council billing.
        services.AddScoped<Application.Abstractions.Municipal.IMunicipalBillingService, Municipal.MunicipalBillingService>();

        // Housing, Land & Estates.
        services.AddScoped<Application.Abstractions.Housing.IHousingService, Housing.HousingService>();

        // School management & fee billing.
        services.AddScoped<Application.Abstractions.Schools.ISchoolBillingService, Schools.SchoolBillingService>();

        // Construction / Job Costing.
        services.AddScoped<Application.Abstractions.JobCosting.IJobCostingService, JobCosting.JobCostingService>();

        // Fixed Assets.
        services.AddScoped<Application.Abstractions.FixedAssets.IFixedAssetService, FixedAssets.FixedAssetService>();

        // HR & Payroll.
        services.AddScoped<Application.Abstractions.Payroll.IPayrollService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Payroll.IEmployeeLoanService, Payroll.EmployeeLoanService>();
        services.AddScoped<Application.Abstractions.Payroll.ILeaveService, Payroll.LeaveService>();
        services.AddScoped<Application.Abstractions.Expenses.IExpenseClaimService, Expenses.ExpenseClaimService>();

        // Budgeting, Planning & Commitment Control.
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetingService, Budgeting.BudgetingService>();

        // Restaurant / Hospitality (optional module).
        services.AddScoped<Application.Abstractions.Restaurant.IRestaurantService, Restaurant.RestaurantService>();

        // Cashiering & Receipting.
        services.AddScoped<Application.Abstractions.Receipting.IPayerAccountProvider, Receipting.ArPayerAccountProvider>();
        services.AddScoped<Application.Abstractions.Receipting.IReceiptingService, Receipting.ReceiptingService>();
        services.AddScoped<Application.Abstractions.Receipting.IBankingService, Receipting.BankingService>();
        services.AddScoped<Application.Abstractions.Receipting.IReceiptImportService, Receipting.ReceiptImportService>();
        services.AddScoped<Application.Abstractions.Receipting.IAgentService, Receipting.AgentService>();
        services.AddScoped<Application.Abstractions.Receipting.IStationeryService, Receipting.StationeryService>();

        // Licensing.
        services.AddSingleton(licensingOptions ?? new LicensingOptions());
        services.AddSingleton<ITrustedLicenceKeys, TrustedLicenceKeys>();
        services.AddSingleton<ILicenceService, LicenceService>();

        // Raw-SQL runner + seeders.
        services.AddSingleton<ISqlScriptRunner>(_ => new SqlScriptRunner(connectionString));
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<AdministratorSeeder>();

        return services;
    }
}
