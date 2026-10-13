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
        LicensingOptions? licensingOptions = null,
        Communications.MessagingOptions? messagingOptions = null,
        Backup.BackupOptions? backupOptions = null,
        FieldEncryptionOptions? fieldEncryption = null,
        Reporting.ReportDeliveryOptions? reportDelivery = null)
    {
        services.AddSingleton(reportDelivery ?? new Reporting.ReportDeliveryOptions());
        // Column encryption for payroll (salary, bank details, national ID): keys from configuration, ONE4ALL_FIELD_KEY or a development key file.
        (FieldEncryptionOptions keys, bool developmentKey) = FieldEncryptionOptions.Resolve(fieldEncryption);
        FieldCipherHolder.Cipher = new FieldCipher(keys);
        services.AddSingleton(FieldCipherHolder.Cipher);
        services.AddSingleton(new FieldEncryptionStatus(keys.ActiveKeyId!, developmentKey));
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, ReencryptFieldsTask>();

        // Cross-cutting.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());

        // Persistence + audit interceptor (scoped, so it sees the ambient user).
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<Licensing.LicenceLimitInterceptor>();
        services.AddSingleton<RowScopeHolder>();
        services.AddDbContext<One4AllDbContext>((sp, options) =>
            options.UseOne4All(connectionString)
                   .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>(), sp.GetRequiredService<Licensing.LicenceLimitInterceptor>()));

        // Security services.
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton(new AuthenticationOptions());
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IDataScopeService, DataScopeService>();
        services.AddScoped<IAuditArchiveService, AuditArchiveService>();
        services.AddScoped<Application.Abstractions.MasterData.IMasterDataService, MasterData.MasterDataService>();
        services.AddScoped<Application.Abstractions.MasterData.IIndustrySetupService, MasterData.IndustrySetupService>();
        services.AddScoped<Application.Abstractions.Onboarding.IOpeningBalanceService, Onboarding.OpeningBalanceService>();
        services.AddScoped<Application.Abstractions.Security.IApiAccessService, Security.ApiAccessService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IApiClientService, ApiClientService>();
        services.AddScoped<IAuditService, Security.AuditService>();

        // Accounting.
        services.AddScoped<IPostingEngine, PostingEngine>();
        services.AddScoped<Accounting.JournalApprovalService>();
        services.AddScoped<IJournalApprovalService>(sp => sp.GetRequiredService<Accounting.JournalApprovalService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Accounting.JournalWorkflowHandler>();
        services.AddScoped(sp => new Lazy<Application.Abstractions.Workflow.IWorkflowService>(() => sp.GetRequiredService<Application.Abstractions.Workflow.IWorkflowService>()));
        services.AddScoped<IOverheadAllocationService, Accounting.OverheadAllocationService>();
        services.AddScoped<IHyperinflationService, Accounting.HyperinflationService>();
        services.AddScoped<IJournalImportService, Accounting.JournalImportService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetControlService, Budgeting.BudgetControlService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetMethodService, Budgeting.BudgetMethodService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetNotificationService, Budgeting.BudgetNotificationService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Budgeting.BudgetNoticesTask>();
        services.AddScoped<Application.Abstractions.Trading.IPromiseToPayService, Trading.PromiseToPayService>();
        services.AddScoped<Application.Abstractions.Trading.ILaybyService, Trading.LaybyService>();
        services.AddScoped<Application.Abstractions.Trading.ICreditLimitService, Trading.CreditLimitService>();
        services.AddScoped<Application.Abstractions.Trading.ICommissionService, Trading.CommissionService>();
        services.AddScoped<Application.Abstractions.Procurement.ISupplierScorecardService, Procurement.SupplierScorecardService>();
        services.AddScoped<Application.Abstractions.Procurement.IRfqService, Procurement.RfqService>();
        services.AddScoped<Application.Abstractions.Procurement.IQualityInspectionService, Procurement.QualityInspectionService>();
        services.AddScoped<Application.Abstractions.Procurement.IInvoiceDisputeService, Procurement.InvoiceDisputeService>();
        services.AddScoped<Application.Abstractions.Procurement.IPurchaseOrderAmendmentService, Procurement.PurchaseOrderAmendmentService>();
        services.AddScoped<Application.Abstractions.Procurement.IDeliveryScheduleService, Procurement.DeliveryScheduleService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetGateService, Budgeting.BudgetGateService>();
        services.AddScoped<Budgeting.BudgetPreparationService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetPreparationService>(sp => sp.GetRequiredService<Budgeting.BudgetPreparationService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Budgeting.BudgetWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetPlanningService, Budgeting.BudgetPlanningService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetWorkbookService, Budgeting.BudgetWorkbookService>();
        services.AddScoped<Application.Abstractions.Procurement.IRebateService, Procurement.RebateService>();
        services.AddScoped<Application.Abstractions.Banking.ITreasuryService, Banking.TreasuryService>();
        services.AddScoped<Application.Abstractions.Vouchers.IPromoService, Vouchers.PromoService>();
        services.AddScoped<Application.Abstractions.Vouchers.IAllowanceVoucherService, Vouchers.AllowanceVoucherService>();
        services.AddScoped<Application.Abstractions.Vouchers.IGiftCardServicing, Vouchers.GiftCardServicing>();
        services.AddScoped<Application.Abstractions.Vouchers.IAccountingVoucherService, Vouchers.AccountingVoucherService>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Vouchers.AccountingVoucherWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Inventory.IStoreRequisitionService, Inventory.StoreRequisitionService>();
        services.AddScoped<Application.Abstractions.Municipal.IIndigentRegisterService, Municipal.IndigentRegisterService>();
        services.AddScoped<Application.Abstractions.Municipal.IServiceOrderService, Municipal.ServiceOrderService>();
        services.AddScoped<Application.Abstractions.Municipal.IClearanceService, Municipal.ClearanceService>();
        services.AddScoped<Application.Abstractions.Municipal.ICouncilLicensingService, Municipal.CouncilLicensingService>();
        services.AddScoped<Application.Abstractions.Municipal.IValuationRollService, Municipal.ValuationRollService>();
        services.AddScoped<Application.Abstractions.Municipal.IMeterReadingService, Municipal.MeterReadingService>();
        services.AddScoped<Application.Abstractions.Municipal.IPrepaidVendingService, Municipal.PrepaidVendingService>();
        services.AddScoped<Application.Abstractions.Municipal.IOwnershipService, Municipal.OwnershipService>();
        services.AddScoped<Municipal.BillingRunService>();
        services.AddScoped<Application.Abstractions.Municipal.IBillingRunService>(sp => sp.GetRequiredService<Municipal.BillingRunService>());
        services.AddScoped<Application.Abstractions.Municipal.IPropertyEnquiryService, Municipal.PropertyEnquiryService>();
        services.AddScoped<Application.Abstractions.Municipal.IReadingRouteService, Municipal.ReadingRouteService>();
        services.AddScoped<Application.Abstractions.Municipal.IServiceDepositService, Municipal.ServiceDepositService>();
        services.AddScoped<Application.Abstractions.Municipal.ITariffCatalogueService, Municipal.TariffCatalogueService>();
        services.AddScoped<Application.Abstractions.Municipal.IFacilityBookingService, Municipal.FacilityBookingService>();
        services.AddScoped<Application.Abstractions.Municipal.ICemeteryService, Municipal.CemeteryService>();
        services.AddScoped<Application.Abstractions.Municipal.IServiceDeskService, Municipal.ServiceDeskService>();
        services.AddScoped<Application.Abstractions.Receipting.IPayerSearchService, Receipting.PayerSearchService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Municipal.ServiceDeskEscalationTask>();
        services.AddScoped<Application.Abstractions.Housing.IWaitingListService, Housing.WaitingListService>();
        services.AddScoped<Housing.AllocationService>();
        services.AddScoped<Application.Abstractions.Housing.IAllocationService>(sp => sp.GetRequiredService<Housing.AllocationService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Housing.AllocationRunWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Housing.StandPriceWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Housing.OwnershipTransferWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Housing.IInstalmentSaleService, Housing.InstalmentSaleService>();
        services.AddScoped<Application.Abstractions.Trading.IReceivableControlService, Trading.ReceivableControlService>();
        services.AddScoped<Application.Abstractions.Housing.IRentalHousingService, Housing.RentalHousingService>();
        services.AddScoped<Application.Abstractions.Housing.IRentalOccupancyService, Housing.RentalOccupancyService>();
        services.AddScoped<Application.Abstractions.Housing.IEvictionService, Housing.EvictionService>();
        services.AddScoped<Application.Abstractions.Housing.IRepossessionService, Housing.RepossessionService>();
        services.AddScoped<Application.Abstractions.Housing.IRentalMaintenanceService, Housing.RentalMaintenanceService>();
        services.AddScoped<Application.Abstractions.Housing.IRentalPropertyAccountingService, Housing.RentalPropertyAccountingService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingEclService, Housing.HousingEclService>();
        services.AddScoped<Application.Abstractions.Housing.IWaitingListControlService, Housing.WaitingListControlService>();
        services.AddScoped<Application.Abstractions.Housing.IAllocationAppealService, Housing.AllocationAppealService>();
        services.AddScoped<Application.Abstractions.Housing.IDevelopmentCovenantService, Housing.DevelopmentCovenantService>();
        services.AddScoped<Application.Abstractions.Housing.IStandLeaseService, Housing.StandLeaseService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingGrantService, Housing.HousingGrantService>();
        services.AddScoped<Application.Abstractions.Housing.ICompensationService, Housing.CompensationService>();
        services.AddScoped<Application.Abstractions.Housing.ICooperativeService, Housing.CooperativeService>();
        services.AddScoped<Application.Abstractions.Housing.IAgreementRegisterService, Housing.AgreementRegisterService>();
        services.AddScoped<Application.Abstractions.Housing.IInformalSettlementService, Housing.InformalSettlementService>();
        services.AddScoped<Application.Abstractions.Housing.IDeveloperAgreementService, Housing.DeveloperAgreementService>();
        services.AddScoped<Application.Abstractions.Housing.IStandBlockService, Housing.StandBlockService>();
        services.AddScoped<Application.Abstractions.Housing.IStandLifecycleService, Housing.StandLifecycleService>();
        services.AddScoped<Application.Abstractions.Onboarding.IVerticalImportService, Onboarding.VerticalImportService>();
        services.AddScoped<Application.Abstractions.Housing.ILandRegisterService, Housing.LandRegisterService>();
        services.AddScoped<Application.Abstractions.Housing.ISchemeProgressService, Housing.SchemeProgressService>();
        services.AddScoped<Application.Abstractions.Housing.IAllocationQuotaService, Housing.AllocationQuotaService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingFeeService, Housing.HousingFeeService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingDepositService, Housing.HousingDepositService>();
        services.AddScoped<Application.Abstractions.Housing.ISchemeBudgetService, Housing.SchemeBudgetService>();
        services.AddScoped<Application.Abstractions.Housing.ITitleDeedService, Housing.TitleDeedService>();
        services.AddScoped<Application.Abstractions.Housing.ITransferApplicationService, Housing.TransferApplicationService>();
        services.AddScoped<Application.Abstractions.Housing.ILandAcquisitionService, Housing.LandAcquisitionService>();
        services.AddScoped<Application.Abstractions.Housing.ILayoutPlanService, Housing.LayoutPlanService>();
        services.AddScoped<Application.Abstractions.Housing.IStandPricingService, Housing.StandPricingService>();
        services.AddScoped<Application.Abstractions.Housing.IDevelopmentLevyService, Housing.DevelopmentLevyService>();
        services.AddScoped<Application.Abstractions.Housing.IJointOwnershipService, Housing.JointOwnershipService>();
        services.AddScoped<Application.Abstractions.Housing.ISectionalTitleService, Housing.SectionalTitleService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingMunicipalLinkService, Housing.HousingMunicipalLinkService>();
        services.AddScoped<Application.Abstractions.Housing.IMapProvider, Housing.RegisterMapProvider>();
        services.AddScoped<Application.Abstractions.Housing.ISpatialRegisterService, Housing.SpatialRegisterService>();
        services.AddScoped<Application.Abstractions.Housing.IServicingCostService, Housing.ServicingCostService>();
        services.AddScoped<Application.Abstractions.Communications.IMessageGateway, Communications.InAppGateway>();

        // Email (SMTP) and SMS (HTTP provider); unconfigured channels refuse sends, so despatch retries and reports them.
        services.AddSingleton(messagingOptions ?? new Communications.MessagingOptions());
        services.AddSingleton(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddScoped<Application.Abstractions.Communications.IMessageGateway, Communications.SmtpEmailGateway>();
        services.AddScoped<Application.Abstractions.Communications.IMessageGateway, Communications.HttpSmsGateway>();
        services.AddScoped<Application.Abstractions.Communications.IMessageGateway, Communications.WhatsAppCloudGateway>();
        services.AddScoped<Application.Abstractions.Communications.IWhatsAppWebhookService, Communications.WhatsAppWebhookService>();
        services.AddSingleton(new Pos.PosDeviceOptions());
        services.AddSingleton<Application.Abstractions.Pos.IDeviceProvider>(sp => new Pos.DeviceProvider(sp.GetRequiredService<Pos.PosDeviceOptions>()));
        services.AddScoped<Application.Abstractions.Integration.IWebhookService, Integration.WebhookService>();
        services.AddScoped<Application.Abstractions.Communications.INotificationService, Communications.NotificationService>();
        if (!services.Any(d => d.ServiceType == typeof(Application.Municipal.IPrepaidTokenGenerator)))
        {
            // A live installation registers its token vendor's generator before this; the placeholder is for testing.
            services.AddSingleton<Application.Municipal.IPrepaidTokenGenerator, Municipal.PlaceholderPrepaidTokenGenerator>();
        }
        services.AddScoped<Trading.DebtCollectionService>();
        services.AddScoped<Application.Abstractions.Trading.IDebtCollectionService>(sp => sp.GetRequiredService<Trading.DebtCollectionService>());
        services.AddScoped<Application.Abstractions.Budgeting.IVarianceCommentaryService, Budgeting.VarianceCommentaryService>();
        services.AddScoped<Application.Abstractions.Trading.ISupplierBankDetailsService, Trading.SupplierBankDetailsService>();
        services.AddScoped<IRecurringJournalService, Accounting.RecurringJournalService>();
        services.AddScoped<IDeferredIncomeService, Accounting.DeferredIncomeService>();
        services.AddScoped<ILedgerIntegrityService, Accounting.LedgerIntegrityService>();
        services.AddScoped<IDataHealthService, Accounting.DataHealthService>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDefinitionService, Reporting.ReportDefinitionService>();
        services.AddScoped<Application.Abstractions.Reporting.IReportSubscriptionService, Reporting.ReportSubscriptionService>();
        services.AddSingleton<Application.Abstractions.Reporting.IReportPrinter, Reporting.ShellReportPrinter>();
        services.AddScoped<Application.Abstractions.Reporting.IAuditedReportRunner, Reporting.AuditedReportRunner>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Reporting.ReportSubscriptionsTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Scheduling.DespatchMessagesTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Scheduling.LapseOffersTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Backup.BackupTask>();
        services.AddScoped<Application.Abstractions.Reporting.IReportingSnapshotService, Reporting.ReportingSnapshotService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Reporting.RefreshSnapshotsTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Backup.ReconcileCommitmentsTask>();
        Backup.BackupOptions backup = backupOptions ?? new Backup.BackupOptions();
        if (string.IsNullOrWhiteSpace(backup.ConnectionString))
        {
            backup.ConnectionString = connectionString;
        }

        services.AddSingleton(backup);
        services.AddScoped<Application.Abstractions.Backup.IBackupService, Backup.BackupService>();
        services.AddScoped<Application.Abstractions.Communications.IStatementDespatchService, Communications.StatementDespatchService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Communications.EmailStatementsTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IJobScheduler, Scheduling.JobScheduler>();
        services.AddSingleton<Application.Modules.IModuleRegistry>(sp => new Application.Modules.ModuleRegistry(sp.GetServices<Application.Modules.IModuleDescriptor>()));
        services.AddScoped<IAccrualService, Accounting.AccrualService>();
        services.AddScoped<IPeriodService, PeriodService>();
        services.AddScoped<Application.Abstractions.Numbering.INumberSequenceService, Infrastructure.Numbering.NumberSequenceService>();
        services.AddScoped<IVatReturnService, Accounting.VatReturnService>();
        services.AddScoped<IPettyCashService, Accounting.PettyCashService>();
        services.AddScoped<IYearEndService, YearEndService>();
        services.AddScoped<ITaxCalculator, TaxCalculator>();
        services.AddScoped<IFxRevaluationService, FxRevaluationService>();
        // The fiscal device: none by default; ONE4ALL_FISCAL_SIMULATE=1 puts the tills on the simulator (a real device registers its own provider).
        services.AddSingleton<IFiscalDeviceProvider>(_ => Environment.GetEnvironmentVariable("ONE4ALL_FISCAL_SIMULATE") == "1"
            ? new SimulatedFiscalDeviceProvider() : new NullFiscalDeviceProvider());
        services.AddScoped<Application.Abstractions.Reporting.IGeneralLedgerReports, GeneralLedgerReports>();
        services.AddScoped<Application.Abstractions.Reporting.IConsolidationService, Accounting.ConsolidationService>();
        services.AddScoped<Application.Abstractions.Reporting.IWorkingCapitalService, Accounting.WorkingCapitalService>();
        services.AddScoped<Application.Abstractions.Reporting.IFinancialReportRenderer, Reporting.FinancialReportRenderer>();
        services.AddScoped<Persistence.Seeding.ReportCatalogueSeeder>();

        // Generic report runner + live data providers (whole-system report binding).
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.GeneralLedgerReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.TradingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.ReceiptingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.SqlReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportFieldDiscovery, Reporting.SqlReportDataProvider>();
        services.AddScoped<Application.Abstractions.Accounting.IReportingTreeService, Accounting.ReportingTreeService>();
        services.AddScoped<Application.Abstractions.Accounting.ITaxComplianceService, Accounting.TaxComplianceService>();
        services.AddScoped<Application.Abstractions.System.ILocalizationService, MasterData.LocalizationService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Accounting.CertificateExpiryTask>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Accounting.ReportingTreeReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.HousingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.MunicipalReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.ReceivablesReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.ProcurementReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.PayablesReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.ControlReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.LedgerReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.CashBookReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.StatementReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.InventoryReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.RetailReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.WeighbridgeReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.AssetTaxReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.DashboardReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.OperationsReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.BudgetingReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.PayrollReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.VerticalReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.PosJobReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportDataProvider, Reporting.RegistersReportDataProvider>();
        services.AddScoped<Application.Abstractions.Reporting.IReportRunner, Reporting.ReportRunner>();
        services.AddScoped<Application.Abstractions.Reporting.IReportParameterLookupService, Reporting.ReportParameterLookupService>();

        // Trading sub-ledgers (AR/AP + Cash Book).
        services.AddScoped<Trading.TradingService>();
        services.AddScoped<Application.Abstractions.Trading.ISalesLedgerService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.IPurchaseLedgerService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.IAllocationService>(sp => sp.GetRequiredService<Trading.TradingService>());
        services.AddScoped<Application.Abstractions.Trading.ITradingReports, Trading.TradingReports>();
        services.AddScoped<Trading.PaymentRunService>();
        services.AddScoped<Application.Abstractions.Trading.IPaymentRunService>(sp => sp.GetRequiredService<Trading.PaymentRunService>());
        services.AddScoped<Application.Abstractions.Trading.IFinanceChargeService, Trading.FinanceChargeService>();
        services.AddScoped<Application.Abstractions.Trading.IDunningService, Trading.DunningService>();
        services.AddScoped<Application.Abstractions.Trading.IRemittanceService, Trading.RemittanceService>();
        services.AddScoped<Application.Abstractions.Trading.ICreditControlService, Trading.CreditControlService>();
        services.AddScoped<Application.Abstractions.Trading.IStatementService, Trading.StatementService>();
        services.AddScoped<Application.Abstractions.Trading.IInvoiceDocumentService, Trading.InvoiceDocumentService>();
        services.AddScoped<Application.Abstractions.Trading.ISupplierStatementService, Trading.SupplierStatementService>();
        services.AddScoped<Application.Abstractions.Trading.IAutoAllocationService, Trading.AutoAllocationService>();
        services.AddScoped<Application.Abstractions.Trading.ITradingRevaluationService, Trading.TradingRevaluationService>();
        services.AddScoped<Application.Abstractions.Trading.IWithholdingTaxService, Trading.WithholdingTaxService>();
        services.AddScoped<Application.Abstractions.Trading.IExpectedCreditLossService, Trading.ExpectedCreditLossService>();
        services.AddScoped<Application.Abstractions.Procurement.IPurchaseOrderDocumentService, Procurement.PurchaseOrderDocumentService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IPickListService, SalesOrders.PickListService>();
        services.AddScoped<Application.Abstractions.Accounting.IIntercompanyService, Accounting.IntercompanyService>();
        services.AddScoped<Application.Abstractions.Accounting.IPeriodCloseService, Accounting.PeriodCloseService>();
        services.AddScoped<Inventory.StocktakeService>();
        services.AddScoped<Application.Abstractions.Inventory.IStocktakeService>(sp => sp.GetRequiredService<Inventory.StocktakeService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Inventory.StocktakeWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Inventory.IReplenishmentService, Inventory.ReplenishmentService>();
        services.AddScoped<Application.Abstractions.Inventory.IStockProvisionService, Inventory.StockProvisionService>();
        services.AddScoped<Application.Abstractions.Inventory.ILandedCostService, Inventory.LandedCostService>();
        services.AddScoped<Application.Abstractions.Inventory.IPricingService, Inventory.PricingService>();
        services.AddScoped<Application.Abstractions.Inventory.IStockAvailabilityService, Inventory.StockAvailabilityService>();
        services.AddScoped<Application.Abstractions.Trading.IRecurringSalesInvoiceService, Trading.RecurringSalesInvoiceService>();
        services.AddScoped<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService, Trading.RecurringPurchaseInvoiceService>();
        services.AddScoped<Application.Abstractions.Banking.IBankReconciliationService, Banking.BankReconciliationService>();
        services.AddScoped<Application.Abstractions.Banking.IChequeService, Banking.ChequeService>();
        services.AddScoped<Application.Abstractions.Banking.IStandingOrderService, Banking.StandingOrderService>();
        services.AddScoped<Application.Abstractions.Documents.IDocumentLayoutService, Documents.DocumentLayoutService>();
        services.AddScoped<Application.Abstractions.Banking.IBankStatementPostingService, Banking.BankStatementPostingService>();
        services.AddScoped<Application.Abstractions.Banking.IBankRuleService, Banking.BankRuleService>();

        // Generic workflow / approvals engine.
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowService, Workflow.WorkflowService>();

        // Inventory + Procurement.
        services.AddScoped<Application.Abstractions.Inventory.IInventoryService, Inventory.InventoryService>();
        services.AddScoped<Application.Abstractions.Inventory.IHeldStockService, Inventory.HeldStockService>();
        services.AddScoped<Application.Abstractions.Inventory.IItemDetailsService, Inventory.ItemDetailsService>();
        services.AddScoped<Application.Abstractions.Inventory.IConcreteTestService, Inventory.ConcreteTestService>();
        services.AddScoped<Application.Abstractions.Municipal.IPropertyRegisterService, Municipal.PropertyRegisterService>();
        services.AddScoped<Application.Abstractions.Inventory.ILotTrackingService, Inventory.LotTrackingService>();
        services.AddScoped<Application.Abstractions.Inventory.IStockTransferService, Inventory.StockTransferService>();
        services.AddScoped<Application.Abstractions.Inventory.IAssemblyService, Inventory.AssemblyService>();
        services.AddScoped<Application.Abstractions.Procurement.IProcurementService, Procurement.ProcurementService>();
        services.AddScoped<Procurement.PurchaseRequisitionService>();
        services.AddScoped<Application.Abstractions.Procurement.IPurchaseRequisitionService>(sp => sp.GetRequiredService<Procurement.PurchaseRequisitionService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Procurement.PurchaseRequisitionWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Trading.PaymentRunWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Municipal.BillingRunWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Trading.WriteOffWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Trading.CreditNoteWorkflowHandler>();
        services.AddScoped<SystemServices.MasterDataChangeService>();
        services.AddScoped<Application.Abstractions.System.IMasterDataChangeService>(sp => sp.GetRequiredService<SystemServices.MasterDataChangeService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, SystemServices.MasterDataChangeWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Procurement.PurchaseOrderWorkflowHandler>();
        services.AddScoped<Schools.SchoolBursaryService>();
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetFrameworkService, Budgeting.BudgetFrameworkService>();
        services.AddScoped<Application.Abstractions.Schools.ISchoolBursaryService>(sp => sp.GetRequiredService<Schools.SchoolBursaryService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Schools.BursaryWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Workflow.WorkflowEscalationTask>();
        services.AddScoped<Trading.CreditNoteApprovalService>();
        services.AddScoped<Application.Abstractions.Trading.ICreditNoteApprovalService>(sp => sp.GetRequiredService<Trading.CreditNoteApprovalService>());
        services.AddScoped<Application.Abstractions.Pos.IPosService, Pos.PosService>();
        services.AddScoped<Application.Abstractions.Pos.IPosCatalogueService, Pos.PosCatalogueService>();
        services.AddScoped<Application.Abstractions.Integration.IIdempotencyStore, Integration.IdempotencyStore>();
        services.AddScoped<Application.Abstractions.Trading.ITraderDirectoryService, Trading.TraderDirectoryService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollSetupService, Payroll.PayrollSetupService>();
        services.AddScoped<Application.Abstractions.Payroll.ITerminationService, Payroll.TerminationService>();
        services.AddScoped<Application.Abstractions.Reporting.IReportBurstService, Reporting.ReportBurstService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Pos.FiscalRetryTask>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Integration.IdempotencyPurgeTask>();
        services.AddScoped<Application.Abstractions.Payroll.ILeavePolicyService, Payroll.LeavePolicyService>();
        services.AddScoped<Application.Abstractions.Payroll.IRosterService, Payroll.RosterService>();
        services.AddScoped<Application.Abstractions.System.IAttachmentService, SystemServices.AttachmentService>();
        services.AddScoped<Application.Abstractions.System.IWorkstationPrinterService, SystemServices.WorkstationPrinterService>();
        services.AddScoped<Application.Abstractions.Banking.ICashForecastService, Banking.CashForecastService>();
        services.AddScoped<Application.Abstractions.SalesOrders.ISalesOrderAmendmentService, SalesOrders.SalesOrderAmendmentService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Payroll.LeaveAccrualTask>();
        services.AddScoped<Application.Abstractions.Pos.IPosEventService, Pos.PosEventService>();
        services.AddScoped<Application.Abstractions.Pos.IPosReceiptService, Pos.PosReceiptService>();
        services.AddScoped<Application.Abstractions.Vouchers.IVoucherService, Vouchers.VoucherService>();
        services.AddScoped<Application.Abstractions.SalesOrders.ISalesOrderService, SalesOrders.SalesOrderService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IDeliveryPlanningService, SalesOrders.DeliveryPlanningService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IQuotationService, SalesOrders.QuotationService>();
        services.AddScoped<Application.Abstractions.Crm.IOpportunityService, Crm.OpportunityService>();
        services.AddScoped<Application.Abstractions.Crm.ICrmService, Crm.CrmService>();

        // Municipality / Council billing.
        services.AddScoped<Application.Abstractions.Municipal.IMunicipalBillingService, Municipal.MunicipalBillingService>();
        services.AddScoped<Application.Abstractions.Municipal.IMunicipalServiceTypeService, Municipal.MunicipalServiceTypeService>();

        // Housing, Land & Estates.
        services.AddScoped<Application.Abstractions.Housing.IHousingService, Housing.HousingService>();

        // School management & fee billing.
        services.AddScoped<Application.Abstractions.Schools.ISchoolBillingService, Schools.SchoolBillingService>();
        services.AddScoped<Application.Abstractions.Schools.ISchoolSetupService, Schools.SchoolSetupService>();
        services.AddScoped<Application.Abstractions.Schools.IAcademicStructureService, Schools.AcademicStructureService>();
        services.AddScoped<Application.Abstractions.Schools.IStudentWalletService, Schools.StudentWalletService>();

        // Construction / Job Costing.
        services.AddScoped<Application.Abstractions.JobCosting.IJobCostingService, JobCosting.JobCostingService>();
        services.AddScoped<Application.Abstractions.JobCosting.IWeighbridgeService, JobCosting.WeighbridgeService>();
        services.AddScoped<Application.Abstractions.JobCosting.IContractControlService, JobCosting.ContractControlService>();
        services.AddScoped<Application.Abstractions.JobCosting.IJobWbsService, JobCosting.JobWbsService>();
        services.AddScoped<Application.Abstractions.JobCosting.ISubcontractService, JobCosting.SubcontractService>();

        // Fixed Assets.
        services.AddScoped<Application.Abstractions.FixedAssets.IFixedAssetService, FixedAssets.FixedAssetService>();
        services.AddScoped<Application.Abstractions.FixedAssets.IAssetControlService, FixedAssets.AssetControlService>();
        services.AddScoped<Application.Abstractions.FixedAssets.ICapitalProjectService, FixedAssets.CapitalProjectService>();

        // HR & Payroll.
        services.AddScoped<Application.Abstractions.Payroll.IPayrollService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollAdjustmentService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollApprovalService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollSettlementService, Payroll.PayrollSettlementService>();
        services.AddScoped<Application.Abstractions.Housing.IInstalmentDefaultService, Housing.InstalmentDefaultService>();
        services.AddScoped<Application.Abstractions.Housing.IHousingRegisterDetailsService, Housing.HousingRegisterDetailsService>();
        services.AddScoped<Application.Abstractions.Housing.IApplicantIntakeService, Housing.ApplicantIntakeService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollVariableService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollRemittanceService, Payroll.PayrollService>();
        services.AddScoped<Application.Abstractions.Banking.IBankFileService, Banking.BankFileService>();
        services.AddScoped<Payroll.EmployeeSelfService>();
        services.AddScoped<Application.Abstractions.Payroll.IEmployeeSelfService>(sp => sp.GetRequiredService<Payroll.EmployeeSelfService>());
        services.AddScoped<Application.Abstractions.Workflow.IWorkflowDocumentHandler, Payroll.LeaveWorkflowHandler>();
        services.AddScoped<Application.Abstractions.Payroll.IPayslipDistributionService, Payroll.PayslipDistributionService>();
        services.AddScoped<Application.Abstractions.Payroll.IPayrollAccountingService, Payroll.PayrollAccountingService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IDeliveryChargeService, SalesOrders.DeliveryChargeService>();
        services.AddScoped<Application.Abstractions.SalesOrders.ICustomerContractService, SalesOrders.CustomerContractService>();
        services.AddScoped<Application.Abstractions.SalesOrders.IDropShipService, SalesOrders.DropShipService>();
        services.AddScoped<Application.Abstractions.Pos.IPromotionService, Pos.PromotionService>();
        services.AddScoped<Application.Abstractions.Pos.ILoyaltyService, Pos.LoyaltyService>();
        services.AddScoped<Application.Abstractions.Pos.ITillCashService, Pos.TillCashService>();
        services.AddScoped<Application.Abstractions.Pos.IParkedSaleService, Pos.ParkedSaleService>();
        services.AddScoped<Application.Abstractions.Inventory.IItemCatalogueService, Inventory.ItemCatalogueService>();
        services.AddScoped<Application.Abstractions.Inventory.IBinService, Inventory.BinService>();
        services.AddScoped<Application.Abstractions.Payroll.IHrRecordsService, Payroll.HrRecordsService>();
        services.AddScoped<Application.Abstractions.Payroll.IEmployeeProfileService, Payroll.EmployeeProfileService>();
        services.AddScoped<Application.Abstractions.Payroll.IHrCasesService, Payroll.HrCasesService>();
        services.AddScoped<Application.Abstractions.Payroll.IHrAlertService, Payroll.HrAlertService>();
        services.AddScoped<Application.Abstractions.Scheduling.IScheduledTask, Payroll.HrExpiryAlertsTask>();
        services.AddScoped<Application.Abstractions.Payroll.IAttendanceService, Payroll.AttendanceService>();
        services.AddScoped<Application.Abstractions.Payroll.IEmployeeLoanService, Payroll.EmployeeLoanService>();
        services.AddScoped<Application.Abstractions.Payroll.IEstablishmentService, Payroll.EstablishmentService>();
        services.AddScoped<Application.Abstractions.Payroll.ILeaveService, Payroll.LeaveService>();
        services.AddScoped<Application.Abstractions.Expenses.IExpenseClaimService, Expenses.ExpenseClaimService>();

        // Budgeting, Planning & Commitment Control.
        services.AddScoped<Application.Abstractions.Budgeting.IBudgetingService, Budgeting.BudgetingService>();
        services.AddScoped<Application.Abstractions.Budgeting.ICapitalCarryForwardService, Budgeting.CapitalCarryForwardService>();

        // Restaurant / Hospitality (optional module).
        services.AddScoped<Application.Abstractions.Restaurant.IRestaurantService, Restaurant.RestaurantService>();
        services.AddScoped<Application.Abstractions.Restaurant.IKitchenPrintService, Restaurant.KitchenPrintService>();
        services.AddScoped<Application.Abstractions.Restaurant.IFloorPlanService, Restaurant.FloorPlanService>();

        // Cashiering & Receipting.
        services.AddScoped<Application.Abstractions.Receipting.IPayerAccountProvider, Receipting.ArPayerAccountProvider>();
        services.AddScoped<Application.Abstractions.Receipting.IReceiptingService, Receipting.ReceiptingService>();
        services.AddScoped<Application.Abstractions.Receipting.IArrangementMonitor, Receipting.ArrangementMonitor>();
        services.AddScoped<Application.Abstractions.Receipting.IBankingService, Receipting.BankingService>();
        services.AddScoped<Application.Abstractions.Receipting.IReceiptImportService, Receipting.ReceiptImportService>();
        services.AddScoped<Application.Abstractions.Receipting.IAgentService, Receipting.AgentService>();
        services.AddScoped<Application.Abstractions.Receipting.IStationeryService, Receipting.StationeryService>();

        // Licensing.
        services.AddSingleton(licensingOptions ?? new LicensingOptions());
        services.AddSingleton<global::One4All.Licensing.Core.IOnlineActivationClient>(sp =>
            sp.GetRequiredService<LicensingOptions>().ActivationUrl is { Length: > 0 } activation && Uri.TryCreate(activation.TrimEnd('/') + "/", UriKind.Absolute, out Uri? service)
                ? new global::One4All.Licensing.Core.HttpOnlineActivationClient(new HttpClient { BaseAddress = service, Timeout = TimeSpan.FromSeconds(30) })
                : new global::One4All.Licensing.Core.OfflineOnlyActivationClient());
        services.AddSingleton<ITrustedLicenceKeys, TrustedLicenceKeys>();
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(services, new Application.Abstractions.System.UpdateOptions());
        services.AddSingleton<Application.Abstractions.System.IUpdateService, SystemServices.UpdateService>();
        services.AddSingleton<IMachineIdProvider, WmiMachineIdProvider>();
        services.AddSingleton<ILicenceService, LicenceService>();
        services.AddScoped<ILicenceUsageService, LicenceUsageService>();

        // Offline sync (outbox + replay engine + handlers).
        services.AddSingleton<Application.Offline.OfflineSyncOptions>();
        services.AddSingleton<Application.Abstractions.Offline.ILocalDataProtector>(_ => Offline.LocalDataProtector.Default());
        services.AddSingleton<Application.Abstractions.Offline.ILocalOfflineQueue>(sp => Offline.FileLocalOfflineQueue.Default(sp.GetRequiredService<Application.Abstractions.Offline.ILocalDataProtector>()));
        services.AddSingleton<Application.Abstractions.Offline.ILocalCatalogueStore>(sp => Offline.FileLocalCatalogueStore.Default(sp.GetRequiredService<Application.Abstractions.Offline.ILocalDataProtector>()));
        services.AddSingleton<Application.Abstractions.Offline.ILocalParkedSaleStore>(sp => Offline.FileLocalParkedSaleStore.Default(sp.GetRequiredService<Application.Abstractions.Offline.ILocalDataProtector>()));
        services.AddScoped<Application.Abstractions.Offline.IOfflineOutbox, Offline.EfOfflineOutbox>();
        services.AddScoped<Application.Abstractions.Offline.IOfflineOperationHandler, Offline.PosSaleOfflineHandler>();
        services.AddScoped<Application.Abstractions.Offline.IOfflineOperationHandler, Offline.PosOpenTillOfflineHandler>();
        services.AddScoped<Application.Abstractions.Offline.IOfflineSyncService, Application.Offline.OfflineSyncService>();
        services.AddScoped<Application.Abstractions.Offline.IOfflineAdminService, Application.Offline.OfflineAdminService>();

        // Raw-SQL runner + seeders.
        services.AddSingleton<ISqlScriptRunner>(_ => new SqlScriptRunner(connectionString));
        services.AddScoped<PermissionSeeder>();
        services.AddScoped<AdministratorSeeder>();

        return services;
    }
}
