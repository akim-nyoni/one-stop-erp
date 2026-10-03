using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Debt handover, summonses and write-off approval over the AR ledger.</summary>
public sealed class DebtCollectionService : IDebtCollectionService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The sales ledger, which writes off the invoices.</param>
    /// <param name="numbers">The number sequences.</param>
    public DebtCollectionService(One4AllDbContext db, ISalesLedgerService ar, INumberSequenceService numbers)
    {
        _db = db;
        _ar = ar;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<string>> HandOverAsync(long companyId, long customerId, string handedOverTo, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => HandOverCoreAsync(companyId, customerId, handedOverTo, date, ct), ct);

    private async Task<Result<string>> HandOverCoreAsync(long companyId, long customerId, string handedOverTo, DateOnly date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handedOverTo))
        {
            return Error.Validation("Debt.NoCollector", "Name the collector or attorney the debt is handed to.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Debt.CustomerNotFound", $"Customer {customerId} not found.");
        }

        List<DebtCase> cases = await _db.Set<DebtCase>().Where(c => c.CustomerId == customerId).ToListAsync(ct).ConfigureAwait(false);
        if (cases.Any(c => c.IsOpen))
        {
            return Error.Conflict("Debt.CaseOpen", "The debtor already has an open case.");
        }

        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.OutstandingBase > 0m)
            .Select(i => new { i.Sign, i.DueDate, i.OutstandingBase })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal overdue = Math.Max(0m, items.Where(i => i.Sign == LedgerSign.Debit && i.DueDate < date).Sum(i => i.OutstandingBase)
            - items.Where(i => i.Sign == LedgerSign.Credit).Sum(i => i.OutstandingBase));
        if (overdue <= 0m)
        {
            return Error.Rule("Debt.NothingOverdue", "The debtor has nothing overdue to hand over.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "DC", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<DebtCase>().Add(new DebtCase(companyId, number.Value, customerId, handedOverTo.Trim(), date, overdue));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RecordSummonsAsync(long companyId, string caseNumber, string summonsNumber, string court, DateOnly issuedOn, DateOnly? hearingDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(summonsNumber) || string.IsNullOrWhiteSpace(court))
        {
            return Result.Failure(Error.Validation("Debt.SummonsIncomplete", "A summons needs its number and the court."));
        }

        Result<DebtCase> found = await OpenCaseAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.RecordSummons(summonsNumber.Trim(), court.Trim(), issuedOn, hearingDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SummonsEntry>> SummonsRegisterAsync(long companyId, CancellationToken ct = default)
        => await (
            from c in _db.Set<DebtCase>().AsNoTracking()
            join customer in _db.Customers.AsNoTracking() on c.CustomerId equals customer.Id
            where c.CompanyId == companyId && c.SummonsNumber != null
            orderby c.SummonsIssuedOn, c.Id
            select new SummonsEntry(c.CaseNumber, customer.Code, c.SummonsNumber!, c.Court!, c.SummonsIssuedOn!.Value, c.HearingDate, c.AmountHandedOver))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> RequestWriteOffAsync(long companyId, string caseNumber, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Debt.ReasonRequired", "A write-off request needs a reason."));
        }

        Result<DebtCase> found = await OpenCaseAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.RequestWriteOff(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<CaseWriteOff>> ApproveWriteOffAsync(long companyId, string caseNumber, long approverUserId, DateOnly date, string badDebtAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveWriteOffCoreAsync(companyId, caseNumber, approverUserId, date, badDebtAccountCode, ct), ct);

    // One unit: every invoice written off and the case closed, or nothing.
    private async Task<Result<CaseWriteOff>> ApproveWriteOffCoreAsync(long companyId, string caseNumber, long approverUserId, DateOnly date, string badDebtAccountCode, CancellationToken ct)
    {
        Result<DebtCase> found = await OpenCaseAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        DebtCase debtCase = found.Value;
        if (debtCase.WriteOffRequestedByUserId is not long requester)
        {
            return Error.Rule("Debt.NoRequest", $"Case {caseNumber} has no write-off request to approve.");
        }

        if (requester == approverUserId)
        {
            return Error.Rule("Debt.SelfApproval", "A different user must approve a write-off than the one who asked for it.");
        }

        List<long> invoices = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == debtCase.CustomerId
                        && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DueDate).Select(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        decimal total = 0m;
        foreach (long invoice in invoices)
        {
            Result<TradingPostingResult> written = await _ar.WriteOffAsync(companyId, invoice, date, badDebtAccountCode, $"Case {caseNumber}: {debtCase.WriteOffReason}", ct).ConfigureAwait(false);
            if (written.IsFailure)
            {
                return written.Error;
            }

            total += written.Value.BaseAmount;
        }

        debtCase.ApproveWriteOff(approverUserId, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new CaseWriteOff(caseNumber, invoices.Count, total);
    }

    /// <inheritdoc />
    public async Task<Result> CloseAsync(long companyId, string caseNumber, DebtCaseStage outcome, DateOnly date, CancellationToken ct = default)
    {
        if (outcome is not (DebtCaseStage.Settled or DebtCaseStage.Withdrawn))
        {
            return Result.Failure(Error.Validation("Debt.Outcome", "A case closes as settled or withdrawn; write-offs need approval."));
        }

        Result<DebtCase> found = await OpenCaseAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Close(outcome, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<DebtCase>> OpenCaseAsync(long companyId, string caseNumber, CancellationToken ct)
    {
        DebtCase? debtCase = await _db.Set<DebtCase>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CaseNumber == caseNumber, ct).ConfigureAwait(false);
        if (debtCase is null)
        {
            return Error.NotFound("Debt.CaseNotFound", $"Case {caseNumber} not found.");
        }

        if (!debtCase.IsOpen)
        {
            return Error.Rule("Debt.CaseClosed", $"Case {caseNumber} is {debtCase.Stage}.");
        }

        return debtCase;
    }
}
