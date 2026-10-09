using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.MasterData;

/// <summary>Creates companies, the chart of accounts, tax codes, currencies, warehouses, dimensions, traders and items (brief §4.17).</summary>
public sealed class MasterDataService : IMasterDataService
{
    private readonly One4AllDbContext _db;
    private readonly ILicenceService? _licence;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="licence">Holds company creation to the licence's company limit.</param>
    public MasterDataService(One4AllDbContext db, ILicenceService? licence = null)
    {
        _db = db;
        _licence = licence;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateCompanyAsync(string code, string name, CurrencyCode baseCurrency, string timeZoneId = "UTC", CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A company needs a code of up to 20 characters and a name.");
        }

        if (!await _db.Currencies.AnyAsync(c => c.Code == baseCurrency, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Master.CurrencyNotFound", $"Currency {baseCurrency} is not set up.");
        }

        if (await _db.Companies.AnyAsync(c => c.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("company", key);
        }

        if (_licence is not null && _licence.WithinLimit("companies", await _db.Companies.CountAsync(ct).ConfigureAwait(false) + 1) is { IsFailure: true } limited)
        {
            return limited.Error;
        }

        Company company = new(key, name.Trim(), baseCurrency, string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim());
        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _db.Branches.Add(new Branch(company.Id, "HO", "Head Office") { IsHeadOffice = true });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return company.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateBranchAsync(long companyId, string code, string name, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A branch needs a code and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Branches.AnyAsync(b => b.CompanyId == companyId && b.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("branch", key);
        }

        Branch branch = new(companyId, key, name.Trim());
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return branch.Id;
    }

    /// <inheritdoc />
    public Task<Result<long>> CreateFiscalYearAsync(long companyId, string code, DateOnly startDate, int months = 12, CancellationToken ct = default)
        => CreateFiscalYearAsync(companyId, code, startDate, FiscalCalendar.Monthly, months, false, ct);

    /// <inheritdoc />
    public async Task<Result<long>> CreateFiscalYearAsync(long companyId, string code, DateOnly startDate, FiscalCalendar calendar, int months, bool adjustmentPeriod, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || months is < 1 or > 24)
        {
            return Invalid("A fiscal year needs a code and between 1 and 24 monthly periods.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        IReadOnlyList<(DateOnly Start, DateOnly End)> periods = FiscalCalendars.Periods(calendar, startDate, months);
        DateOnly endDate = periods[^1].End;
        List<FiscalYear> years = await _db.FiscalYears.Where(y => y.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        if (years.Any(y => y.Code == key))
        {
            return Duplicate("fiscal year", key);
        }

        if (years.Any(y => y.StartDate <= endDate && y.EndDate >= startDate))
        {
            return Error.Conflict("Master.YearOverlaps", "The year overlaps an existing fiscal year.");
        }

        FiscalYear year = new(companyId, key, startDate, endDate);
        for (int n = 0; n < periods.Count; n++)
        {
            year.AddPeriod(n + 1, calendar == FiscalCalendar.FourFourFive ? $"P{n + 1} {key}" : periods[n].Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture), periods[n].Start, periods[n].End);
        }

        if (adjustmentPeriod)
        {
            year.AddAdjustmentPeriod();
        }

        _db.FiscalYears.Add(year);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return year.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAccountAsync(NewAccount account, CancellationToken ct = default)
    {
        if (Key(account.Code, 8) is not string key || string.IsNullOrWhiteSpace(account.Name))
        {
            return Invalid("An account needs a code of up to 8 characters and a name.");
        }

        if (await CompanyMissingAsync(account.CompanyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Accounts.AnyAsync(a => a.CompanyId == account.CompanyId && a.AccountCode == key, ct).ConfigureAwait(false))
        {
            return Duplicate("account", key);
        }

        LedgerAccount created = new(account.CompanyId, key, account.Name.Trim(), account.Type, string.IsNullOrWhiteSpace(account.ReportCategory) ? null : account.ReportCategory.Trim());
        if (account.ControlType is ControlAccountType control)
        {
            created.MakeControlAccount(control);
        }

        created.BudgetEnabled = account.BudgetEnabled;
        _db.Accounts.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateTaxCodeAsync(NewTaxCode taxCode, CancellationToken ct = default)
    {
        if (Key(taxCode.Code, 20) is not string key || string.IsNullOrWhiteSpace(taxCode.Name) || taxCode.RatePercent is < 0m or > 100m)
        {
            return Invalid("A tax code needs a code, a name and a rate between 0 and 100%.");
        }

        if (await _db.TaxCodes.AnyAsync(t => t.CompanyId == taxCode.CompanyId && t.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("tax code", key);
        }

        foreach (string? accountCode in new[] { taxCode.OutputAccountCode, taxCode.InputAccountCode })
        {
            if (await AccountMissingAsync(taxCode.CompanyId, accountCode, ct).ConfigureAwait(false) is Error missing)
            {
                return missing;
            }
        }

        TaxCode created = new(taxCode.CompanyId, key, taxCode.Name.Trim(), taxCode.Type)
        {
            OutputAccountCode = Code(taxCode.OutputAccountCode), InputAccountCode = Code(taxCode.InputAccountCode), IsActive = true,
        };
        created.AddRate(taxCode.EffectiveFrom, taxCode.RatePercent);
        _db.TaxCodes.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result> CreateCurrencyAsync(CurrencyCode code, string name, string symbol, int decimalPlaces = 2, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || decimalPlaces is < 0 or > 4)
        {
            return Result.Failure(Error.Validation("Master.Invalid", "A currency needs a name and 0 to 4 decimal places."));
        }

        if (await _db.Currencies.AnyAsync(c => c.Code == code, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Master.Duplicate", $"Currency {code} already exists."));
        }

        decimal increment = decimalPlaces == 0 ? 1m : 1m / (decimal)Math.Pow(10, decimalPlaces);
        _db.Currencies.Add(new Currency(code, name.Trim(), string.IsNullOrWhiteSpace(symbol) ? code.Value : symbol.Trim(), decimalPlaces, increment));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetExchangeRateAsync(CurrencyCode from, CurrencyCode to, ExchangeRateType type, DateOnly effectiveFrom, decimal rate, CancellationToken ct = default)
    {
        if (rate <= 0m || from == to)
        {
            return Result.Failure(Error.Validation("Master.Invalid", "A rate is positive and between two different currencies."));
        }

        if (await _db.Currencies.CountAsync(c => c.Code == from || c.Code == to, ct).ConfigureAwait(false) < 2)
        {
            return Result.Failure(Error.NotFound("Master.CurrencyNotFound", "Both currencies must be set up."));
        }

        List<ExchangeRate> sameDay = await _db.ExchangeRates.Where(r => r.FromCurrency == from && r.ToCurrency == to && r.RateType == type && r.EffectiveFrom == effectiveFrom)
            .ToListAsync(ct).ConfigureAwait(false);
        _db.ExchangeRates.RemoveRange(sameDay);
        _db.ExchangeRates.Add(new ExchangeRate(from, to, type, effectiveFrom, rate));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateWarehouseAsync(long companyId, string code, string name, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A warehouse needs a code and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Warehouses.AnyAsync(w => w.CompanyId == companyId && w.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("warehouse", key);
        }

        Warehouse warehouse = new(companyId, key, name.Trim());
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return warehouse.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateCostCentreAsync(long companyId, string code, string name, CancellationToken ct = default)
    {
        if (Key(code, 3) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A cost centre needs a code of up to 3 characters and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.CostCentres.AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("cost centre", key);
        }

        CostCentre centre = new(companyId, key, name.Trim());
        _db.CostCentres.Add(centre);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return centre.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateDepartmentAsync(long companyId, string code, string name, CancellationToken ct = default)
    {
        if (Key(code, 3) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A department needs a code of up to 3 characters and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Departments.AnyAsync(d => d.CompanyId == companyId && d.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("department", key);
        }

        Department department = new(companyId, key, name.Trim());
        _db.Departments.Add(department);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return department.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SaveFundAsync(long companyId, string code, string name, bool isRestricted, bool isActive = true, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A fund needs a code (up to 20 characters) and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        Fund? fund = await _db.Funds.FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == key, ct).ConfigureAwait(false);
        if (fund is null)
        {
            fund = new Fund(companyId, key, name.Trim(), isRestricted);
            _db.Funds.Add(fund);
        }

        fund.Name = name.Trim();
        fund.IsRestricted = isRestricted;
        fund.IsActive = isActive;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fund.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SaveProjectAsync(long companyId, string code, string name, string? fundCode, bool isActive = true, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A project needs a code (up to 20 characters) and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        string? fund = Key(fundCode ?? string.Empty, 20);
        if (fund is not null && !await _db.Funds.AnyAsync(f => f.CompanyId == companyId && f.Code == fund, ct).ConfigureAwait(false))
        {
            return Error.NotFound("MasterData.FundNotFound", $"Fund {fund} not found.");
        }

        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (project is null)
        {
            project = new Project(companyId, key, name.Trim());
            _db.Projects.Add(project);
        }

        project.Name = name.Trim();
        project.FundCode = fund;
        project.IsActive = isActive;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return project.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateCustomerAsync(long companyId, string code, string name, CurrencyCode currency, decimal creditLimit = 0m, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name) || creditLimit < 0m)
        {
            return Invalid("A customer needs a code, a name and a credit limit that is not negative.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Customers.AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("customer", key);
        }

        Customer customer = new(companyId, key, name.Trim(), currency) { CreditLimit = creditLimit };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return customer.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateSupplierAsync(long companyId, string code, string name, CurrencyCode currency, CancellationToken ct = default)
    {
        if (Key(code, 20) is not string key || string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A supplier needs a code and a name.");
        }

        if (await CompanyMissingAsync(companyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Suppliers.AnyAsync(s => s.CompanyId == companyId && s.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("supplier", key);
        }

        Supplier supplier = new(companyId, key, name.Trim(), currency);
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return supplier.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateItemAsync(NewItem item, CancellationToken ct = default)
    {
        if (Key(item.Code, 40) is not string key || string.IsNullOrWhiteSpace(item.Description) || item.StandardCost < 0m)
        {
            return Invalid("An item needs a code, a description and any standard cost not negative.");
        }

        if (await CompanyMissingAsync(item.CompanyId, ct).ConfigureAwait(false) is Error missing)
        {
            return missing;
        }

        if (await _db.Items.AnyAsync(i => i.CompanyId == item.CompanyId && i.Code == key, ct).ConfigureAwait(false))
        {
            return Duplicate("item", key);
        }

        foreach (string? accountCode in new[] { item.RevenueAccountCode, item.CostOfSalesAccountCode })
        {
            if (await AccountMissingAsync(item.CompanyId, accountCode, ct).ConfigureAwait(false) is Error noAccount)
            {
                return noAccount;
            }
        }

        Item created = new(item.CompanyId, key, item.Description.Trim(), item.Type)
        {
            RevenueAccountCode = Code(item.RevenueAccountCode), CostOfSalesAccountCode = Code(item.CostOfSalesAccountCode), StandardCost = item.StandardCost,
        };
        _db.Items.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    private static string? Key(string? code, int maxLength)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        return key.Length is > 0 && key.Length <= maxLength ? key : null;
    }

    private static string? Code(string? accountCode) => string.IsNullOrWhiteSpace(accountCode) ? null : accountCode.Trim().ToUpperInvariant();

    private static Error Invalid(string message) => Error.Validation("Master.Invalid", message);

    private static Error Duplicate(string what, string key) => Error.Conflict("Master.Duplicate", $"A {what} {key} already exists.");

    private async Task<Error?> CompanyMissingAsync(long companyId, CancellationToken ct)
        => await _db.Companies.AnyAsync(c => c.Id == companyId, ct).ConfigureAwait(false) ? null : Error.NotFound("Master.CompanyNotFound", $"Company {companyId} not found.");

    private async Task<Error?> AccountMissingAsync(long companyId, string? accountCode, CancellationToken ct)
    {
        string? code = Code(accountCode);
        return code is null || await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct).ConfigureAwait(false)
            ? null
            : Error.NotFound("Master.AccountNotFound", $"Account {code} does not exist.");
    }
}
