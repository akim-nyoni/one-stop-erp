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
    public MasterDataExportService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public IReadOnlyList<string> Kinds { get; } = ["Accounts", "Warehouses", "Customers", "Suppliers", "Items", "Employees"];

    /// <inheritdoc />
    public async Task<Result<string>> ExportAsync(long companyId, string kind, CancellationToken ct = default)
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
                return ImportTemplates.Csv("employee_number,name,basic_salary,engaged,national_id,department,cost_centre,grade",
                    employees.Select(e => new object?[] { e.EmployeeNumber, e.FullName, e.BasicSalary, e.EngagementDate, e.NationalIdNumber, e.Department, e.CostCentre, e.Grade }));
            default:
                return Error.Validation("Import.ExportKind", $"Nothing to export for {kind}; one of {string.Join(", ", Kinds)}.");
        }
    }
}
