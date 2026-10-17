using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Licensing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Licensing;

/// <summary>What the installation uses against each licence limit (for the status screen), counted across every company.</summary>
public sealed class LicenceUsageService : ILicenceUsageService
{
    private readonly One4AllDbContext _db;
    private readonly ILicenceService _licence;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="licence">The licence.</param>
    public LicenceUsageService(One4AllDbContext db, ILicenceService licence)
    {
        _db = db;
        _licence = licence;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenceUsage>> UsageAsync(CancellationToken ct = default)
    {
        List<LicenceUsage> usage = [];
        async Task Add(string key, string? module, IQueryable<object> rows)
        {
            usage.Add(new LicenceUsage(key, module, await rows.CountAsync(ct).ConfigureAwait(false), Limit(key, module)));
        }

        int? Limit(string key, string? module) => module is null ? _licence.GetLimit(key)
            : _licence.Current.GetModuleLimit(module, key)
                ?? (key.StartsWith(module + "_", StringComparison.OrdinalIgnoreCase) ? _licence.Current.GetModuleLimit(module, key[(module.Length + 1)..]) : null)
                ?? _licence.GetLimit(key);

        await Add("companies", null, _db.Companies.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("warehouses", null, _db.Warehouses.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("pos_terminals", LicenceModules.PointOfSale, _db.PosTerminals.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("properties", LicenceModules.Municipal, _db.Properties.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("stands", LicenceModules.Housing, _db.HousingStands.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("waiting_list", LicenceModules.Housing, _db.HousingApplicants.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("students", LicenceModules.School, _db.Students.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        await Add("employees", LicenceModules.Payroll, _db.Employees.IgnoreQueryFilters([One4AllDbContext.CompanyFilter])).ConfigureAwait(false);
        // "payrolls" (PAY): the companies running a payroll, i.e. with employees on the books.
        int payrolls = await _db.Employees.IgnoreQueryFilters([One4AllDbContext.CompanyFilter]).Where(e => e.Status != Core.Payroll.EmployeeStatus.Terminated).Select(e => e.CompanyId).Distinct().CountAsync(ct).ConfigureAwait(false);
        usage.Add(new LicenceUsage("payrolls", LicenceModules.Payroll, payrolls, Limit("payrolls", LicenceModules.Payroll)));
        return usage;
    }
}
