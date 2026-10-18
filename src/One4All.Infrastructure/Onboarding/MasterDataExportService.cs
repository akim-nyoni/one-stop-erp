using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Onboarding;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Onboarding;

/// <summary>Writes the master-data registers in their import layouts.</summary>
public sealed class MasterDataExportService : IMasterDataExportService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="audit">Records each export.</param>
    /// <param name="user">The user exporting (salaries and national IDs are left out unless they may see salaries).</param>
    public MasterDataExportService(One4AllDbContext db, Application.Abstractions.Security.IAuditService? audit = null, Application.Abstractions.Security.ICurrentUser? user = null)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    private readonly Application.Abstractions.Security.IAuditService? _audit;
    private readonly Application.Abstractions.Security.ICurrentUser? _user;

    // Every export of personal or payroll data is recorded (brief §4.15.10: a payroll export is a data-breach risk).
    private async Task RecordExportAsync(long companyId, string module, string entityType, string? entityId, string details, CancellationToken ct)
    {
        if (_audit is not null)
        {
            await _audit.RecordAsync(new Application.Abstractions.Security.AuditEvent(companyId, _user?.UserId, _user?.UserName ?? "system", module, entityType, entityId,
                Core.Audit.AuditAction.Export, Details: details, Terminal: _user?.Terminal), ct).ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public IReadOnlyList<string> Kinds { get; } = ["Accounts", "Warehouses", "Customers", "Suppliers", "Items", "Employees", "Stock", "FixedAssets", "StaffLoans", "CostCentres", "Departments",
        "Students", "Stands"];

    /// <inheritdoc />
    public async Task<Result<string>> ExportAsync(long companyId, string kind, CancellationToken ct = default)
    {
        Result<string> exported = await ExportCoreAsync(companyId, kind, ct).ConfigureAwait(false);
        if (exported.IsSuccess)
        {
            await RecordExportAsync(companyId, "Onboarding", "MasterDataExport", (kind ?? string.Empty).Trim(), $"{exported.Value.Count(ch => ch == '\n')} line(s)", ct).ConfigureAwait(false);
        }

        return exported;
    }

    private async Task<Result<string>> ExportCoreAsync(long companyId, string kind, CancellationToken ct)
    {
        switch ((kind ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "ACCOUNTS":
                var accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("code,name,type,category,manual,budget",
                    accounts.Select(a => new object?[] { a.AccountCode, a.Name, a.Type.ToString(), a.ReportCategory, a.AllowManualJournal, a.BudgetEnabled }));
            case "WAREHOUSES":
                var warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId).OrderBy(w => w.Code).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("code,name", warehouses.Select(w => new object?[] { w.Code, w.Name }));
            case "CUSTOMERS":
                var customers = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("code,name,currency,credit_limit", customers.Select(c => new object?[] { c.Code, c.Name, c.Currency.Value, c.CreditLimit }));
            case "SUPPLIERS":
                var suppliers = await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("code,name,currency", suppliers.Select(s => new object?[] { s.Code, s.Name, s.Currency.Value }));
            case "ITEMS":
                var items = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).OrderBy(i => i.Code).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("code,description,type,revenue_account,cost_account,category",
                    items.Select(i => new object?[] { i.Code, i.Description, i.ItemType.ToString(), i.RevenueAccountCode, i.CostOfSalesAccountCode, i.Category }));
            case "EMPLOYEES":
                var employees = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId).OrderBy(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false);
                // Pay and national IDs only for a user who may see salaries (an unattended export, with no user, has them).
                bool salaries = _user is not { IsAuthenticated: true } signedIn || signedIn.Permissions.Has("Payroll.ViewSalary", Core.Security.OperationFlags.View);
                return ImportTemplates.Csv("employee_number,name,basic_salary,engaged,national_id,department,cost_centre,grade",
                    employees.Select(e => new object?[] { e.EmployeeNumber, e.FullName, salaries ? e.BasicSalary : null, e.EngagementDate, salaries ? e.NationalIdNumber : null, e.Department, e.CostCentre, e.Grade }));
            case "STOCK":
                // Quantities on hand at their average cost, by item and warehouse (a stock take-on file).
                var stock = await (from b in _db.StockBalances.AsNoTracking()
                                   join i in _db.Items.AsNoTracking() on b.ItemId equals i.Id
                                   join w in _db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
                                   where b.CompanyId == companyId && b.QuantityOnHand != 0m
                                   orderby i.Code, w.Code
                                   select new { Item = i.Code, Warehouse = w.Code, b.QuantityOnHand, b.AverageCost }).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("item,warehouse,quantity,unit_cost", stock.Select(s => new object?[] { s.Item, s.Warehouse, s.QuantityOnHand, s.AverageCost }));
            case "FIXEDASSETS":
                var assets = await _db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.AssetNumber).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("asset_number,description,class,acquired,cost,accumulated,life_months,cost_account,accum_account,expense_account,residual,method,rate",
                    assets.Select(a => new object?[] { a.AssetNumber, a.Description, a.AssetClass, a.AcquisitionDate, a.Cost, a.AccumulatedDepreciation, a.UsefulLifeMonths, a.CostAccountCode,
                        a.AccumulatedDepreciationAccountCode, a.DepreciationExpenseAccountCode, a.ResidualValue, a.Method.ToString(), a.ReducingBalanceRatePercent == 0m ? null : a.ReducingBalanceRatePercent }));
            case "STAFFLOANS":
                var loans = await (from l in _db.EmployeeLoans.AsNoTracking()
                                   join e in _db.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                                   where l.CompanyId == companyId && l.Outstanding > 0m
                                   orderby e.EmployeeNumber, l.Reference
                                   select new { e.EmployeeNumber, l.Reference, l.Outstanding, l.Installment, l.LoanReceivableAccountCode }).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("employee_number,reference,outstanding,instalment,loan_account",
                    loans.Select(l => new object?[] { l.EmployeeNumber, l.Reference, l.Outstanding, l.Installment, l.LoanReceivableAccountCode }));
            case "COSTCENTRES":
                return ImportTemplates.Csv("code,name", (await _db.CostCentres.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => new object?[] { x.Code, x.Name }));
            case "DEPARTMENTS":
                return ImportTemplates.Csv("code,name", (await _db.Departments.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => new object?[] { x.Code, x.Name }));
            case "STUDENTS":
                var students = await (from s in _db.Students.AsNoTracking()
                                      join g in _db.Customers.AsNoTracking() on s.GuardianCustomerId equals g.Id
                                      where s.CompanyId == companyId && s.IsEnrolled
                                      orderby s.AdmissionNumber
                                      select new { s.AdmissionNumber, s.FullName, s.Grade, Guardian = g.Code, s.IsBoarder, s.AdmittedOn }).ToListAsync(ct).ConfigureAwait(false);
                return ImportTemplates.Csv("admission,name,grade,guardian,boarder,admitted",
                    students.Select(s => new object?[] { s.AdmissionNumber, s.FullName, s.Grade, s.Guardian, s.IsBoarder, s.AdmittedOn }));
            case "STANDS":
                Dictionary<long, string> parcels = await _db.LandParcels.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.ParcelNumber, ct).ConfigureAwait(false);
                Dictionary<long, string> schemes = await _db.HousingSchemes.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Code, ct).ConfigureAwait(false);
                return ImportTemplates.Csv("stand,parcel,size,cost,scheme,status",
                    (await _db.HousingStands.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.StandNumber).ToListAsync(ct).ConfigureAwait(false)).Select(x => new object?[]
                    {
                        x.StandNumber, parcels.GetValueOrDefault(x.LandParcelId), x.SizeSquareMetres, x.AllocatedCost, x.SchemeId is long scheme ? schemes.GetValueOrDefault(scheme) : null, x.Status.ToString(),
                    }));
            default:
                return Error.Validation("Import.ExportKind", $"Nothing to export for {kind}; one of {string.Join(", ", Kinds)}.");
        }
    }
}
