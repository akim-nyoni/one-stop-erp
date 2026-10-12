using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// The generic report runner (brief §13): resolves a report definition by code (company → system, falling
/// back to a built-in system definition), fetches live rows from the <see cref="IReportDataProvider"/> that
/// handles the definition's data-source command, and renders to the requested format through the banded
/// engine. One entry point for every report in the catalogue, whatever module owns it.
/// </summary>
public sealed class ReportRunner : IReportRunner
{
    private readonly One4AllDbContext _db;
    private readonly IEnumerable<IReportDataProvider> _providers;
    private readonly ReportEngine _engine = new();

    /// <summary>Creates the runner.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="providers">The registered data providers.</param>
    public ReportRunner(One4AllDbContext db, IEnumerable<IReportDataProvider> providers)
    {
        _db = db;
        _providers = providers;
    }

    /// <summary>Creates the runner for a signed-in user, whose own report versions come first.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="providers">The data providers.</param>
    /// <param name="currentUser">The signed-in user.</param>
    public ReportRunner(One4AllDbContext db, IEnumerable<IReportDataProvider> providers, Application.Abstractions.Security.ICurrentUser currentUser)
        : this(db, providers) => _userId = currentUser?.UserId;

    private readonly long? _userId;

    /// <inheritdoc />
    public Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, CancellationToken ct = default)
        => RenderAsync(companyId, reportCode, parameters, format, new HashSet<Application.Security.SensitiveField>(), ct);

    /// <inheritdoc />
    public Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format,
        IReadOnlySet<Application.Security.SensitiveField> hiddenFields, CancellationToken ct = default)
        => RenderAsync(companyId, reportCode, parameters, format, Application.Reporting.ReportAccess.Unrestricted with { Hidden = hiddenFields }, ct);

    /// <inheritdoc />
    public async Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format,
        Application.Reporting.ReportAccess access, CancellationToken ct = default)
    {
        IReadOnlySet<Application.Security.SensitiveField> hiddenFields = access.Hidden;
        ReportDefinition? definition = await ResolveDefinitionAsync(companyId, reportCode, ct).ConfigureAwait(false)
            ?? BuiltInDefinition(reportCode);
        if (definition is null)
        {
            return Error.NotFound("Report.NotFound", $"No report definition found for code '{reportCode}'.");
        }

        if (!access.MayRun(definition.Module, definition.RequiredPermission))
        {
            return Error.Forbidden("Rpt.ModuleNotAuthorised", $"Report {reportCode} needs the {definition.RequiredPermission ?? definition.Module + " reports"} permission.");
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = Application.Reporting.ReportRowScope.Apply(
            await RowsAsync(companyId, reportCode, definition, parameters, hiddenFields, ct).ConfigureAwait(false), access.Scope);

        var company = await _db.Companies.Where(c => c.Id == companyId).Select(c => new { c.Name, c.Locale, c.Logo }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Dictionary<string, string> terms = await _db.TerminologyOverrides.AsNoTracking().Where(t => t.CompanyId == companyId)
            .ToDictionaryAsync(t => t.Term, t => t.Replacement, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);

        // The company's locale formats numbers and dates and picks label translations; its terms replace the standard words on labels.
        ReportInput input = new()
        {
            Definition = definition,
            Rows = rows,
            CompanyName = company?.Name ?? string.Empty,
            Parameters = parameters,
            Culture = company?.Locale is string locale ? System.Globalization.CultureInfo.GetCultureInfo(locale) : null,
            Terminology = terms,
            CompanyLogo = company?.Logo,
            SubReports = await SubReportsAsync(companyId, definition, rows, parameters, access, 0, new SubReportBudget(), ct).ConfigureAwait(false),
        };

        return Render(input, format, reportCode.ToLowerInvariant());
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BurstSlice>>> BurstAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, string keyField,
        Application.Reporting.ReportAccess access, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyField))
        {
            return Error.Validation("Rpt.BurstKey", "Bursting needs the field to split the report by.");
        }

        ReportDefinition? definition = await ResolveDefinitionAsync(companyId, reportCode, ct).ConfigureAwait(false) ?? BuiltInDefinition(reportCode);
        if (definition is null)
        {
            return Error.NotFound("Report.NotFound", $"No report definition found for code '{reportCode}'.");
        }

        if (!access.MayRun(definition.Module, definition.RequiredPermission))
        {
            return Error.Forbidden("Rpt.ModuleNotAuthorised", $"Report {reportCode} needs the {definition.RequiredPermission ?? definition.Module + " reports"} permission.");
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = Application.Reporting.ReportRowScope.Apply(
            await RowsAsync(companyId, reportCode, definition, parameters, access.Hidden, ct).ConfigureAwait(false), access.Scope);
        if (rows.Count > 0 && !rows[0].ContainsKey(keyField.Trim()))
        {
            return Error.Validation("Rpt.BurstKey", $"Report {reportCode} has no field {keyField.Trim()} to split by.");
        }

        var company = await _db.Companies.Where(c => c.Id == companyId).Select(c => new { c.Name, c.Locale, c.Logo }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Dictionary<string, string> terms = await _db.TerminologyOverrides.AsNoTracking().Where(t => t.CompanyId == companyId)
            .ToDictionaryAsync(t => t.Term, t => t.Replacement, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        Func<string, IReadOnlyDictionary<string, object?>, ReportInput?>? subReports = await SubReportsAsync(companyId, definition, rows, parameters, access, 0, new SubReportBudget(), ct).ConfigureAwait(false);
        List<BurstSlice> slices = [];
        foreach (IGrouping<string, IReadOnlyDictionary<string, object?>> group in rows.GroupBy(r => Convert.ToString(r.GetValueOrDefault(keyField.Trim()), System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty)
                     .Where(g => g.Key.Length > 0).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            List<IReadOnlyDictionary<string, object?>> slice = [.. group];
            ReportInput input = new()
            {
                Definition = definition, Rows = slice, CompanyName = company?.Name ?? string.Empty, Parameters = parameters,
                Culture = company?.Locale is string locale ? System.Globalization.CultureInfo.GetCultureInfo(locale) : null, Terminology = terms, CompanyLogo = company?.Logo, SubReports = subReports,
            };
            string safeKey = new([.. group.Key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')]);
            Result<RenderedDocument> rendered = Render(input, format, $"{reportCode.ToLowerInvariant()}-{safeKey.ToLowerInvariant()}");
            if (rendered.IsFailure)
            {
                return rendered.Error;
            }

            slices.Add(new BurstSlice(group.Key, rendered.Value, slice));
        }

        return slices;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(long companyId, string reportCode, ReportDefinition definition, IReadOnlyDictionary<string, object?> parameters,
        IReadOnlySet<Application.Security.SensitiveField> hiddenFields, CancellationToken ct)
    {
        string command = definition.DataSources.Count == 0 ? reportCode : ReportCommands.For(definition);
        IReportDataProvider? provider = _providers.FirstOrDefault(p => p.CanHandle(command));
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = provider is null
            ? []
            : await provider.GetRowsAsync(companyId, command, ReportParameterValues.ForQuery(definition, parameters), ct).ConfigureAwait(false);
        rows = Application.Reporting.ReportRowFilters.Apply(rows, parameters);
        return Application.Security.FieldSensitivity.Strip(rows, hiddenFields);
    }

    // How many child runs one report may fetch in all, across every level of nesting.
    private sealed class SubReportBudget
    {
        public int Remaining { get; set; } = 500;
    }

    // Each sub-report is fetched once per distinct parameter set (a per-row band links its parameters from every row), nested
    // sub-reports in turn, so the engine can lay them out without touching the database.
    private async Task<Func<string, IReadOnlyDictionary<string, object?>, ReportInput?>?> SubReportsAsync(long companyId, ReportDefinition definition,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> parameters, Application.Reporting.ReportAccess access,
        int depth, SubReportBudget budget, CancellationToken ct)
    {
        List<(ReportElement Element, bool PerRow)> elements = [.. SubReports.ElementsOf(definition)];
        if (elements.Count == 0 || depth >= SubReports.MaxDepth)
        {
            return null;
        }

        Dictionary<string, ReportInput?> fetched = new(StringComparer.Ordinal);
        Dictionary<string, ReportDefinition?> definitions = new(StringComparer.OrdinalIgnoreCase);
        foreach ((ReportElement element, bool perRow) in elements)
        {
            string code = element.SubReportCode!.Trim();
            if (!definitions.TryGetValue(code, out ReportDefinition? child))
            {
                child = await ResolveDefinitionAsync(companyId, code, ct).ConfigureAwait(false) ?? BuiltInDefinition(code);
                definitions[code] = child;
            }

            IReadOnlyDictionary<string, object?>?[] single = [null];
            IEnumerable<IReadOnlyDictionary<string, object?>?> sources = perRow ? rows.Cast<IReadOnlyDictionary<string, object?>?>() : single;
            foreach (IReadOnlyDictionary<string, object?>? row in sources)
            {
                IReadOnlyDictionary<string, object?> childParameters = SubReports.ParametersFor(element, parameters, row);
                string key = SubReports.Key(code, childParameters);
                if (fetched.ContainsKey(key))
                {
                    continue;
                }

                if (child is null || budget.Remaining <= 0 || !access.MayRun(child.Module, child.RequiredPermission))
                {
                    fetched[key] = null;
                    continue;
                }

                budget.Remaining--;
                IReadOnlyList<IReadOnlyDictionary<string, object?>> childRows = Application.Reporting.ReportRowScope.Apply(
                    await RowsAsync(companyId, code, child, childParameters, access.Hidden, ct).ConfigureAwait(false), access.Scope);
                fetched[key] = new ReportInput
                {
                    Definition = child, Rows = childRows, Parameters = childParameters,
                    SubReports = await SubReportsAsync(companyId, child, childRows, childParameters, access, depth + 1, budget, ct).ConfigureAwait(false),
                };
            }
        }

        return (code, childParameters) => fetched.GetValueOrDefault(SubReports.Key(code, childParameters));
    }

    private Result<RenderedDocument> Render(ReportInput input, ReportFormat format, string baseName)
    {
        if (format == ReportFormat.Csv)
        {
            byte[] csv = new CsvReportExporter().Export(input.Rows);
            return new RenderedDocument(csv, "text/csv", $"{baseName}.csv");
        }

        if (format == ReportFormat.Xlsx)
        {
            byte[] xlsx = new XlsxReportExporter().Export(input.Rows, input.Definition.Name);
            return new RenderedDocument(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{baseName}.xlsx");
        }

        RenderModel model = _engine.Build(input);
        (byte[] bytes, string contentType, string ext) = format switch
        {
            ReportFormat.Pdf => (new PdfReportRenderer().Render(model), "application/pdf", "pdf"),
            ReportFormat.Text => (new PlainTextReportRenderer().Render(model), "text/plain", "txt"),
            ReportFormat.Layout => (ReportLayout.Serialize(model), "application/vnd.one4all.layout+json", "layout.json"),
            ReportFormat.XlsxLayout => (LayoutGrid.ToXlsx(model), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            ReportFormat.CsvLayout => (LayoutGrid.ToCsv(model), "text/csv", "csv"),
            ReportFormat.Docx => (LayoutGrid.ToDocx(model), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
            _ => (new HtmlReportRenderer().Render(model), "text/html", "html"),
        };

        return new RenderedDocument(bytes, contentType, $"{baseName}.{ext}");
    }

    /// <summary>The definition a code runs: the company's version, else the system one, else a built-in report.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The report code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The definition, or null.</returns>
    internal static async Task<ReportDefinition?> DefinitionAsync(One4AllDbContext db, long companyId, string code, CancellationToken ct)
        => await ResolveDefinitionAsync(db, companyId, code, ct).ConfigureAwait(false) ?? BuiltInDefinition(code);

    private Task<ReportDefinition?> ResolveDefinitionAsync(long companyId, string code, CancellationToken ct) => ResolveDefinitionAsync(_db, companyId, code, ct, _userId);

    // Resolution order (brief §13.2): the user's own version, then the company's, then the system's.
    private static async Task<ReportDefinition?> ResolveDefinitionAsync(One4AllDbContext db, long companyId, string code, CancellationToken ct, long? userId = null)
    {
        ReportDefinitionRecord? record = await db.ReportDefinitions
            .Where(r => r.Code == code && (r.Scope == ReportScope.System || (r.Scope == ReportScope.Company && r.CompanyId == companyId)
                || (userId != null && r.Scope == ReportScope.User && r.CompanyId == companyId && r.OwnerUserId == userId)))
            .OrderByDescending(r => r.Scope)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return record is null ? null : ReportDefinitionSerializer.Deserialize(record.DefinitionJson);
    }

    private static ReportDefinition? BuiltInDefinition(string code) => code switch
    {
        SystemReports.TrialBalanceCode => SystemReports.TrialBalance(),
        SystemReports.CustomerStatementCode => SystemReports.CustomerStatement(),
        SystemReports.IncomeStatementCode => SystemReports.IncomeStatement(),
        SystemReports.BalanceSheetCode => SystemReports.BalanceSheet(),
        SystemReports.CashFlowCode => SystemReports.CashFlow(),
        SystemReports.DepartmentalIncomeStatementCode => SystemReports.DepartmentalIncomeStatement(),
        SystemReports.TrialBalanceByTypeCode => SystemReports.TrialBalanceByType(),
        SystemReports.JournalDaybookCode => SystemReports.JournalDaybook(),
        SystemReports.TrialBalanceMovementCode => SystemReports.TrialBalanceMovement(),
        SystemReports.ChartOfAccountsCode => SystemReports.ChartOfAccounts(),
        SystemReports.FinancialSnapshotCode => SystemReports.FinancialSnapshot(),
        SystemReports.VatReturnCode => SystemReports.VatReturn(),
        SystemReports.ControlReconciliationCode => SystemReports.ControlReconciliation(),
        SystemReports.VatReturnByCodeCode => SystemReports.VatReturnByCode(),
        SystemReports.CustomerRegisterCode => SystemReports.CustomerRegister(),
        SystemReports.SupplierRegisterCode => SystemReports.SupplierRegister(),
        SystemReports.AgedDebtorsCode => SystemReports.AgedDebtors(),
        SystemReports.AgedCreditorsCode => SystemReports.AgedCreditors(),
        SystemReports.CreditExposureCode => SystemReports.CreditExposure(),
        SystemReports.CashFlowForecastCode => SystemReports.CashFlowForecast(),
        SystemReports.PaymentsDueCode => SystemReports.PaymentsDue(),
        SystemReports.StockValuationCode => SystemReports.StockValuation(),
        SystemReports.StockMovementsCode => SystemReports.StockMovements(),
        SystemReports.ReorderCode => SystemReports.Reorder(),
        SystemReports.BackOrdersCode => SystemReports.BackOrders(),
        SystemReports.SlowStockCode => SystemReports.SlowStock(),
        SystemReports.StockAgeingCode => SystemReports.StockAgeing(),
        SystemReports.AbcAnalysisCode => SystemReports.AbcAnalysis(),
        SystemReports.ItemMasterCode => SystemReports.ItemMaster(),
        SystemReports.AssetRegisterCode => SystemReports.AssetRegister(),
        SystemReports.DepreciationForecastCode => SystemReports.DepreciationForecast(),
        SystemReports.AssetMaintenanceCode => SystemReports.AssetMaintenance(),
        SystemReports.AssetDisposalsCode => SystemReports.AssetDisposals(),
        SystemReports.PayrollSummaryCode => SystemReports.PayrollSummary(),
        SystemReports.EmployeeLoansCode => SystemReports.EmployeeLoans(),
        SystemReports.PayrollDeductionsCode => SystemReports.PayrollDeductions(),
        SystemReports.EmployeeRegisterCode => SystemReports.EmployeeRegister(),
        SystemReports.LeaveBalancesCode => SystemReports.LeaveBalances(),
        SystemReports.PropertyRegisterCode => SystemReports.PropertyRegister(),
        SystemReports.BudgetVsActualCode => SystemReports.BudgetVsActual(),
        SystemReports.BudgetListCode => SystemReports.BudgetList(),
        SystemReports.RecurringJournalsCode => SystemReports.RecurringJournals(),
        SystemReports.PeriodStatusCode => SystemReports.PeriodStatus(),
        SystemReports.PipelineCode => SystemReports.Pipeline(),
        SystemReports.ExpenseClaimsCode => SystemReports.ExpenseClaims(),
        SystemReports.PosSalesCode => SystemReports.PosSales(),
        SystemReports.JobProfitabilityCode => SystemReports.JobProfitability(),
        SystemReports.ProgressCertificatesCode => SystemReports.ProgressCertificates(),
        SystemReports.JobWipCode => SystemReports.JobWip(),
        SystemReports.HousingStandsCode => SystemReports.HousingStands(),
        SystemReports.StudentRegisterCode => SystemReports.StudentRegister(),
        SystemReports.OpenPurchaseOrdersCode => SystemReports.OpenPurchaseOrders(),
        SystemReports.PurchaseRequisitionsCode => SystemReports.PurchaseRequisitions(),
        SystemReports.QuotationsCode => SystemReports.Quotations(),
        SystemReports.SalesBackOrdersCode => SystemReports.SalesBackOrders(),
        SystemReports.RecurringInvoicesCode => SystemReports.RecurringInvoices(),
        SystemReports.RecurringPurchasesCode => SystemReports.RecurringPurchases(),
        SystemReports.MenuCode => SystemReports.Menu(),
        SystemReports.TableStatusCode => SystemReports.TableStatus(),
        SystemReports.KitchenTicketsCode => SystemReports.KitchenTickets(),
        SystemReports.TipAllocationsCode => SystemReports.TipAllocations(),
        SystemReports.MenuEngineeringCode => SystemReports.MenuEngineering(),
        SystemReports.ReservationsCode => SystemReports.Reservations(),
        SystemReports.WaitlistCode => SystemReports.Waitlist(),
        SystemReports.TableGroupsCode => SystemReports.TableGroups(),
        SystemReports.FloorPlanCode => SystemReports.FloorPlan(),
        SystemReports.SectionSalesCode => SystemReports.SectionSales(),
        SystemReports.VoidsAndCompsCode => SystemReports.VoidsAndComps(),
        SystemReports.OrderTypeSalesCode => SystemReports.OrderTypeSales(),
        SystemReports.DeliveriesCode => SystemReports.Deliveries(),
        SystemReports.ServerSalesCode => SystemReports.ServerSales(),
        SystemReports.CategorySalesCode => SystemReports.CategorySales(),
        SystemReports.WastageCode => SystemReports.Wastage(),
        SystemReports.BillsCode => SystemReports.Bills(),
        SystemReports.MenuCostCode => SystemReports.MenuCost(),
        _ => ReceiptingReports.All().FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)),
    };
}
