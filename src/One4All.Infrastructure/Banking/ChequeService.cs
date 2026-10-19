using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Keeps the cheque register and captured supplier statements.</summary>
public sealed class ChequeService : IChequeService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="purchases">Reverses the supplier payment a cancelled cheque paid.</param>
    public ChequeService(One4AllDbContext db, Application.Abstractions.Trading.IPurchaseLedgerService? purchases = null)
    {
        _db = db;
        _purchases = purchases;
    }

    private readonly Application.Abstractions.Trading.IPurchaseLedgerService? _purchases;

    /// <inheritdoc />
    public async Task<Result<long>> IssueAsync(long companyId, long bankAccountId, string chequeNumber, DateOnly issuedOn, string payee, decimal amount, string? documentNumber, CancellationToken ct = default)
    {
        if (amount <= 0m || string.IsNullOrWhiteSpace(payee))
        {
            return Error.Validation("Bank.ChequeInvalid", "A cheque needs a payee and a positive amount.");
        }

        return await AddAsync(companyId, bankAccountId, chequeNumber, issuedOn, payee.Trim(), amount, documentNumber?.Trim(), ChequeStatus.Issued, null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<long>> VoidLeafAsync(long companyId, long bankAccountId, string chequeNumber, DateOnly date, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Bank.ReasonRequired", "Voiding a cheque leaf needs a reason.");
        }

        return await AddAsync(companyId, bankAccountId, chequeNumber, date, "(void)", 0m, null, ChequeStatus.Voided, reason.Trim(), ct).ConfigureAwait(false);
    }

    private async Task<Result<long>> AddAsync(long companyId, long bankAccountId, string chequeNumber, DateOnly date, string payee, decimal amount, string? documentNumber, ChequeStatus status, string? reason,
        CancellationToken ct)
    {
        string number = chequeNumber.Trim();
        if (number.Length == 0)
        {
            return Error.Validation("Bank.ChequeNumber", "A cheque needs its number.");
        }

        if (!await _db.BankAccounts.AnyAsync(b => b.Id == bankAccountId && b.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Bank.AccountNotFound", $"Bank account {bankAccountId} not found.");
        }

        if (await _db.Cheques.AnyAsync(c => c.BankAccountId == bankAccountId && c.ChequeNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Bank.ChequeExists", $"Cheque {number} is already in the register.");
        }

        Cheque cheque = new(companyId, bankAccountId, number, date, payee, amount, documentNumber, status);
        if (reason is not null)
        {
            cheque.Move(status, date, reason);
        }

        _db.Cheques.Add(cheque);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cheque.Id;
    }

    /// <inheritdoc />
    public Task<Result> PresentAsync(long companyId, long chequeId, DateOnly date, CancellationToken ct = default) => MoveAsync(companyId, chequeId, ChequeStatus.Presented, date, null, ct);

    /// <inheritdoc />
    public Task<Result> CancelAsync(long companyId, long chequeId, DateOnly date, string reason, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(reason)
            ? Task.FromResult(Result.Failure(Error.Validation("Bank.ReasonRequired", "Cancelling a cheque needs a reason.")))
            : _db.AtomicAsync(async () =>
            {
                Result moved = await MoveAsync(companyId, chequeId, ChequeStatus.Cancelled, date, reason.Trim(), ct).ConfigureAwait(false);
                if (moved.IsFailure)
                {
                    return moved;
                }

                // A cheque that paid a supplier: the payment never happened, so it is reversed (the invoices owed again, the bank put back).
                string? paid = await _db.Cheques.Where(c => c.Id == chequeId).Select(c => c.DocumentNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (_purchases is not null && paid is not null
                    && await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsPayable && i.ItemType == Core.Trading.OpenItemType.Payment
                        && i.DocumentNumber == paid, ct).ConfigureAwait(false))
                {
                    Result<string> reversed = await _purchases.ReversePaymentAsync(companyId, paid, date, $"Cheque cancelled: {reason.Trim()}", ct).ConfigureAwait(false);
                    if (reversed.IsFailure)
                    {
                        return Result.Failure(reversed.Error);
                    }
                }

                return Result.Success();
            }, ct);

    private async Task<Result> MoveAsync(long companyId, long chequeId, ChequeStatus status, DateOnly date, string? reason, CancellationToken ct)
    {
        Cheque? cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (cheque is null)
        {
            return Result.Failure(Error.NotFound("Bank.ChequeNotFound", $"Cheque {chequeId} not found."));
        }

        if (cheque.Status != ChequeStatus.Issued)
        {
            return Result.Failure(Error.Rule("Bank.ChequeNotIssued", $"Cheque {cheque.ChequeNumber} is {cheque.Status.ToString().ToLowerInvariant()}; only an issued cheque can move on."));
        }

        if (date < cheque.IssuedOn)
        {
            return Result.Failure(Error.Validation("Bank.ChequeDate", "A cheque cannot clear or be stopped before it was written."));
        }

        cheque.Move(status, date, reason);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CaptureSupplierStatementAsync(long companyId, long supplierId, DateOnly statementDate, decimal closingBalance, IReadOnlyList<StatementLineInput> lines,
        CancellationToken ct = default)
    {
        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        if (lines.Any(l => string.IsNullOrWhiteSpace(l.DocumentNumber)))
        {
            return Error.Validation("Ap.StatementLine", "Every statement line needs the document number.");
        }

        if (lines.Sum(l => l.Amount) != closingBalance)
        {
            return Error.Validation("Ap.StatementTotal", $"The lines total {lines.Sum(l => l.Amount):0.00} but the statement shows {closingBalance:0.00}.");
        }

        SupplierStatement statement = new(companyId, supplierId, statementDate, closingBalance);
        foreach (StatementLineInput line in lines)
        {
            statement.AddLine(line.DocumentNumber.Trim(), line.DocumentDate, line.Amount);
        }

        _db.SupplierStatements.Add(statement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return statement.Id;
    }
}
