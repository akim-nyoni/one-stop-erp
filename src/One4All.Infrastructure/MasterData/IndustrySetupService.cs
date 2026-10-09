using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.MasterData;

/// <summary>Sets companies up from industry templates through the master data service (brief §12).</summary>
public sealed class IndustrySetupService : IIndustrySetupService
{
    private static readonly string[] Ledgers = ["GL", "AR", "AP", "CB", "INV", "POP", "SOP", "POS", "VOU", "FA", "PAY", "JOB", "MUNI", "HSE", "SCH", "BUD", "RES"];

    private readonly One4AllDbContext _db;
    private readonly IMasterDataService _master;
    private readonly IPeriodService _periods;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="master">Creates the master data.</param>
    /// <param name="periods">Opens the first year.</param>
    public IndustrySetupService(One4AllDbContext db, IMasterDataService master, IPeriodService periods)
    {
        _db = db;
        _master = master;
        _periods = periods;
    }

    /// <inheritdoc />
    public async Task<Result<IndustrySetupResult>> SetUpCompanyAsync(IndustrySetupRequest request, CancellationToken ct = default)
    {
        if (request.VatRatePercent is < 0m or > 100m)
        {
            return Error.Validation("Master.Invalid", "The VAT rate is between 0 and 100%.");
        }

        Result<long> company = await _master.CreateCompanyAsync(request.Code, request.Name, request.BaseCurrency, ct: ct).ConfigureAwait(false);
        if (company.IsFailure)
        {
            return company.Error;
        }

        Result<long> year = await _master.CreateFiscalYearAsync(company.Value, request.FiscalYearStart.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), request.FiscalYearStart, ct: ct)
            .ConfigureAwait(false);
        if (year.IsFailure)
        {
            return year.Error;
        }

        Result opened = await _periods.OpenYearAsync(company.Value, year.Value, Ledgers, ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        IndustrySetupResult total = new(company.Value, 0, 0, 0, 0, 0);
        foreach (VerticalTemplate template in IndustryTemplates.Plan(request.Verticals))
        {
            Result<IndustrySetupResult> applied = await ApplyAsync(company.Value, template, request.VatRatePercent, request.BaseCurrency, request.WithDemoData, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return applied.Error;
            }

            total = Add(total, applied.Value);
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<Result<IndustrySetupResult>> AddVerticalAsync(long companyId, Vertical vertical, decimal vatRatePercent = 15m, bool withDemoData = false, CancellationToken ct = default)
    {
        CurrencyCode? currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => (CurrencyCode?)c.BaseCurrency).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (currency is null)
        {
            return Error.NotFound("Master.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Core first, so an industry's accounts and taxes find what they rely on.
        IndustrySetupResult total = new(companyId, 0, 0, 0, 0, 0);
        foreach (VerticalTemplate template in IndustryTemplates.Plan([vertical]))
        {
            Result<IndustrySetupResult> applied = await ApplyAsync(companyId, template, vatRatePercent, currency.Value, withDemoData && template.Vertical == vertical, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return applied.Error;
            }

            total = Add(total, applied.Value);
        }

        return total;
    }

    private static IndustrySetupResult Add(IndustrySetupResult a, IndustrySetupResult b)
        => a with
        {
            Accounts = a.Accounts + b.Accounts, TaxCodes = a.TaxCodes + b.TaxCodes, Warehouses = a.Warehouses + b.Warehouses, Roles = a.Roles + b.Roles, DemoRecords = a.DemoRecords + b.DemoRecords,
            ItemCategories = a.ItemCategories + b.ItemCategories, Terminals = a.Terminals + b.Terminals, FeaturedReports = a.FeaturedReports + b.FeaturedReports,
        };

    // The vertical's item categories, POS terminals at head office and report set; anything already there is kept as it is.
    private async Task<(int Categories, int Terminals, int Reports)> ApplyExtrasAsync(long companyId, Vertical vertical, CancellationToken ct)
    {
        VerticalExtras extras = IndustryExtras.For(vertical);
        int categories = 0, terminals = 0, reports = 0;
        foreach ((string code, string name) in extras.ItemCategories)
        {
            string key = code.ToUpperInvariant();
            if (!await _db.ItemCategories.AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
            {
                _db.ItemCategories.Add(new Core.Inventory.ItemCategory(companyId, code, name));
                categories++;
            }
        }

        long? headOffice = await _db.Branches.Where(b => b.CompanyId == companyId).OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.Id).Select(b => (long?)b.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        foreach (TerminalSeed seed in extras.Terminals)
        {
            if (headOffice is long branch && !await _db.Set<Core.Pos.PosTerminal>().AnyAsync(t => t.CompanyId == companyId && t.Code == seed.Code, ct).ConfigureAwait(false))
            {
                _db.Set<Core.Pos.PosTerminal>().Add(new Core.Pos.PosTerminal(companyId, branch, seed.Code, seed.Name));
                terminals++;
            }
        }

        foreach (string code in extras.FeaturedReports)
        {
            if (!await _db.FeaturedReports.AnyAsync(f => f.CompanyId == companyId && f.ReportCode == code, ct).ConfigureAwait(false))
            {
                _db.FeaturedReports.Add(new Core.Reporting.FeaturedReport(companyId, code, vertical.ToString()));
                reports++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (categories, terminals, reports);
    }

    // Counts a creation, treats an existing record as already done, and stops on anything else.
    private static Error? Tally(Result result, ref int count)
    {
        if (result.IsSuccess)
        {
            count++;
            return null;
        }

        return result.Error.Code == "Master.Duplicate" ? null : result.Error;
    }

    private async Task<Result<IndustrySetupResult>> ApplyAsync(long companyId, VerticalTemplate template, decimal vatRate, CurrencyCode currency, bool demo, CancellationToken ct)
    {
        int accounts = 0, taxes = 0, warehouses = 0, roles = 0, records = 0;
        foreach (AccountSeed account in template.Accounts)
        {
            if (Tally(await _master.CreateAccountAsync(new NewAccount(companyId, account.Code, account.Name, account.Type, account.Control, account.ReportCategory, account.BudgetEnabled), ct).ConfigureAwait(false), ref accounts) is Error e)
            {
                return e;
            }
        }

        DateOnly from = await _db.FiscalYears.Where(y => y.CompanyId == companyId).OrderBy(y => y.StartDate).Select(y => (DateOnly?)y.StartDate).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new DateOnly(DateTime.UtcNow.Year, 1, 1);
        foreach (TaxSeed tax in template.TaxCodes)
        {
            if (Tally(await _master.CreateTaxCodeAsync(new NewTaxCode(companyId, tax.Code, tax.Name, tax.Type, tax.Rate ?? vatRate, from, tax.OutputAccount, tax.InputAccount), ct).ConfigureAwait(false),
                ref taxes) is Error e)
            {
                return e;
            }
        }

        foreach ((string code, string name) in template.Warehouses)
        {
            if (Tally(await _master.CreateWarehouseAsync(companyId, code, name, ct).ConfigureAwait(false), ref warehouses) is Error e)
            {
                return e;
            }
        }

        // Roles are shared across companies: a starter role that already exists is kept as its owners have shaped it.
        foreach (RoleSeed seed in template.Roles)
        {
            string normalized = seed.Name.ToUpperInvariant();
            if (await _db.Roles.AnyAsync(r => r.NormalizedName == normalized, ct).ConfigureAwait(false))
            {
                continue;
            }

            Role role = new(seed.Name, $"Starter role ({template.Vertical})");
            foreach ((string scope, GrantLevel level) in seed.Grants)
            {
                foreach (PermissionDefinition permission in PermissionCatalogue.All.Where(p => string.Equals(p.Code, scope, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Module, scope, StringComparison.OrdinalIgnoreCase)))
                {
                    role.Grant(permission.Code, IndustryTemplates.Operations(permission.Available, level));
                }
            }

            _db.Roles.Add(role);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            roles++;
        }

        if (demo)
        {
            foreach ((string code, string name) in template.DemoCustomers)
            {
                if (Tally(await _master.CreateCustomerAsync(companyId, code, name, currency, ct: ct).ConfigureAwait(false), ref records) is Error e)
                {
                    return e;
                }
            }

            foreach ((string code, string name) in template.DemoSuppliers)
            {
                if (Tally(await _master.CreateSupplierAsync(companyId, code, name, currency, ct).ConfigureAwait(false), ref records) is Error e)
                {
                    return e;
                }
            }

            foreach (ItemSeed item in template.DemoItems)
            {
                Result<long> created = await _master.CreateItemAsync(new NewItem(companyId, item.Code, item.Description, item.Type, item.RevenueAccount, item.CostAccount), ct).ConfigureAwait(false);
                if (Tally(created, ref records) is Error e)
                {
                    return e;
                }

                if (created.IsSuccess)
                {
                    (await _db.Items.SingleAsync(i => i.Id == created.Value, ct).ConfigureAwait(false)).Category = item.Category;
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
        }

        (int itemCategories, int terminals, int featured) = await ApplyExtrasAsync(companyId, template.Vertical, ct).ConfigureAwait(false);
        return new IndustrySetupResult(companyId, accounts, taxes, warehouses, roles, records, itemCategories, terminals, featured);
    }
}
