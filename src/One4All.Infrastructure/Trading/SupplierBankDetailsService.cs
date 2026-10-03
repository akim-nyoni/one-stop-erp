using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Dual authorisation of supplier bank-detail changes.</summary>
public sealed class SupplierBankDetailsService : ISupplierBankDetailsService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SupplierBankDetailsService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> RequestChangeAsync(long companyId, long supplierId, SupplierBankDetails details, long requestedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (string.IsNullOrWhiteSpace(details.AccountNumber))
        {
            return Error.Validation("Ap.BankDetailsIncomplete", "New bank details need an account number.");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        if (await _db.Set<SupplierBankChange>().AnyAsync(c => c.SupplierId == supplierId && c.Status == BankChangeStatus.Pending, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Ap.BankChangePending", $"Supplier {supplier.Code} already has a bank-detail change awaiting approval.");
        }

        SupplierBankChange change = new(companyId, supplierId, Clean(details.AccountName), details.AccountNumber.Trim(), Clean(details.BankName), requestedByUserId);
        _db.Set<SupplierBankChange>().Add(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return change.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingBankChange>> PendingAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await (
            from c in _db.Set<SupplierBankChange>().AsNoTracking()
            join s in _db.Suppliers.AsNoTracking() on c.SupplierId equals s.Id
            where c.CompanyId == companyId && c.Status == BankChangeStatus.Pending
            orderby c.Id
            select new { c, s.Code, s.BankAccountName, s.BankAccountNumber, s.BankName })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new PendingBankChange(r.c.Id, r.c.SupplierId, r.Code,
            new SupplierBankDetails(r.BankAccountName, r.BankAccountNumber, r.BankName),
            new SupplierBankDetails(r.c.AccountName, r.c.AccountNumber, r.c.BankName),
            r.c.RequestedByUserId))];
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long changeId, long approverUserId, CancellationToken ct = default)
    {
        Result<SupplierBankChange> found = await FindPendingAsync(companyId, changeId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        SupplierBankChange change = found.Value;
        if (!change.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Ap.SelfApproval", "A different user must approve a bank-detail change than the one who requested it."));
        }

        Supplier supplier = await _db.Suppliers.FirstAsync(s => s.Id == change.SupplierId, ct).ConfigureAwait(false);
        supplier.ChangeBankDetails(change);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long changeId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Ap.ReasonRequired", "Rejecting a bank-detail change requires a reason."));
        }

        Result<SupplierBankChange> found = await FindPendingAsync(companyId, changeId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Reject(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<SupplierBankChange>> FindPendingAsync(long companyId, long changeId, CancellationToken ct)
    {
        SupplierBankChange? change = await _db.Set<SupplierBankChange>().FirstOrDefaultAsync(c => c.Id == changeId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Error.NotFound("Ap.BankChangeNotFound", $"Bank-detail change {changeId} not found.");
        }

        if (change.Status != BankChangeStatus.Pending)
        {
            return Error.Rule("Ap.BankChangeNotPending", $"Bank-detail change {changeId} is already {change.Status}.");
        }

        return change;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
