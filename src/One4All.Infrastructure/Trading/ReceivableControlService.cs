using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Keeps customers' and tenure types' receivables control accounts.</summary>
public sealed class ReceivableControlService : IReceivableControlService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ReceivableControlService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetCustomerControlAsync(long companyId, string customerCode, string? accountCode, CancellationToken ct = default)
    {
        string code = (customerCode ?? string.Empty).Trim().ToUpperInvariant();
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == code, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {code} not found."));
        }

        return await ApplyAsync(_db, companyId, customer, string.IsNullOrWhiteSpace(accountCode) ? null : accountCode.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetTenureControlAsync(long companyId, InstalmentTenure tenure, string? accountCode, CancellationToken ct = default)
    {
        TenureControlAccount? mapping = await _db.Set<TenureControlAccount>().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Tenure == tenure, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            if (mapping is not null)
            {
                _db.Remove(mapping);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result.Success();
        }

        string account = accountCode.Trim().ToUpperInvariant();
        if (!await IsReceivableControlAsync(_db, companyId, account, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Validation("Ar.NotReceivableControl", $"{account} is not a receivables control account."));
        }

        if (mapping is null)
        {
            _db.Set<TenureControlAccount>().Add(new TenureControlAccount(companyId, tenure, account));
        }
        else
        {
            mapping.AccountCode = account;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(InstalmentTenure Tenure, string AccountCode)>> TenureControlsAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<TenureControlAccount>().AsNoTracking().Where(m => m.CompanyId == companyId).OrderBy(m => m.Tenure).ToListAsync(ct).ConfigureAwait(false))
            .Select(m => (m.Tenure, m.AccountCode))];

    /// <summary>Puts a buyer on their tenure type's control account (if one is set) before their first instalment-sale item posts.</summary>
    /// <returns>Success, or why the buyer cannot move to it.</returns>
    internal static async Task<Result> ApplyTenureAsync(One4AllDbContext db, long companyId, long customerId, InstalmentTenure tenure, CancellationToken ct)
    {
        string? account = await db.Set<TenureControlAccount>().Where(m => m.CompanyId == companyId && m.Tenure == tenure).Select(m => m.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null)
        {
            return Result.Success();
        }

        Customer customer = await db.Customers.FirstAsync(c => c.Id == customerId, ct).ConfigureAwait(false);
        return string.Equals(customer.ReceivableControlAccountCode, account, StringComparison.OrdinalIgnoreCase) ? Result.Success()
            : await ApplyAsync(db, companyId, customer, account, ct).ConfigureAwait(false);
    }

    /// <summary>The company's main receivables control account: the lowest-numbered one.</summary>
    internal static Task<string?> MainControlAsync(One4AllDbContext db, long companyId, CancellationToken ct)
        => db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.AccountsReceivable).OrderBy(a => a.AccountCode).Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct);

    private static Task<bool> IsReceivableControlAsync(One4AllDbContext db, long companyId, string account, CancellationToken ct)
        => db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account && a.ControlAccountType == ControlAccountType.AccountsReceivable, ct);

    private static async Task<Result> ApplyAsync(One4AllDbContext db, long companyId, Customer customer, string? account, CancellationToken ct)
    {
        if (account is not null && !await IsReceivableControlAsync(db, companyId, account, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Validation("Ar.NotReceivableControl", $"{account} is not a receivables control account."));
        }

        CurrencyCode baseCurrency = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        if (account is not null && customer.Currency != baseCurrency)
        {
            return Result.Failure(Error.Rule("Ar.ControlCurrency", $"Customer {customer.Code} trades in {customer.Currency}; a dedicated control is for base-currency debtors."));
        }

        if (await db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Ar.ControlHasItems", $"Customer {customer.Code} already has items on its control account; open a new account for the other control."));
        }

        customer.ReceivableControlAccountCode = account;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
