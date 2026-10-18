using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <inheritdoc cref="IWriteOffRequestService" />
public sealed class WriteOffRequestService : IWriteOffRequestService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">Writes the debt off once approved.</param>
    /// <param name="workflow">The approval workflow.</param>
    public WriteOffRequestService(One4AllDbContext db, ISalesLedgerService ar, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _ar = ar;
        _workflow = workflow;
    }

    /// <summary>The workflow reference of a request (debt-collection cases use their case numbers).</summary>
    /// <param name="requestId">The request.</param>
    /// <returns>The reference.</returns>
    public static string Reference(long requestId) => "WO-" + requestId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public Task<Result<long>> RequestAsync(long companyId, long openItemId, string badDebtAccountCode, string reason, int minimumDaysOverdue, long requestedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(badDebtAccountCode) || string.IsNullOrWhiteSpace(reason))
            {
                return Error.Validation("Ar.WriteOffRequest", "A write-off needs the bad debt account and the reason.");
            }

            OpenItem? invoice = await _db.OpenItems.AsNoTracking().FirstOrDefaultAsync(o => o.Id == openItemId && o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable, ct)
                .ConfigureAwait(false);
            if (invoice is null || invoice.Sign != LedgerSign.Debit || invoice.OutstandingTransaction <= 0m)
            {
                return Error.Rule("Ar.WriteOffNothingOpen", "Only a customer invoice with something still open can be written off.");
            }

            if (await _db.Set<WriteOffRequest>().AnyAsync(r => r.CompanyId == companyId && r.OpenItemId == openItemId && r.Status == WriteOffRequestStatus.Pending, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Ar.WriteOffPending", $"{invoice.DocumentNumber} already has a write-off waiting for approval.");
            }

            WriteOffRequest request = new(companyId, openItemId, invoice.DocumentNumber, invoice.OutstandingTransaction, badDebtAccountCode.Trim().ToUpperInvariant(), reason.Trim(),
                Math.Max(0, minimumDaysOverdue), requestedByUserId);
            _db.Set<WriteOffRequest>().Add(request);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            string? customer = await _db.Customers.AsNoTracking().Where(c => c.Id == invoice.TraderId).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.WriteOff, Reference(request.Id), invoice.OutstandingBase,
                requestedByUserId, [("Customer", customer), ("Reason", request.Reason)], ct).ConfigureAwait(false);
            return governed.IsFailure ? governed.Error : Result<long>.Success(request.Id);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<VatReliefWriteOff?>> DecideAsync(long companyId, long requestId, bool approve, long userId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.WriteOff, Reference(requestId), ct).ConfigureAwait(false))
        {
            return Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.WriteOff, Reference(requestId));
        }

        return await _db.AtomicAsync(() => DecideCoreAsync(companyId, requestId, approve, userId, date, reason, ct), ct).ConfigureAwait(false);
    }

    internal async Task<Result<VatReliefWriteOff?>> DecideCoreAsync(long companyId, long requestId, bool approve, long userId, DateOnly date, string? reason, CancellationToken ct)
    {
        WriteOffRequest? request = await _db.Set<WriteOffRequest>().FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (request is null || request.Status != WriteOffRequestStatus.Pending)
        {
            return Error.NotFound("Ar.WriteOffNotPending", $"No write-off request {requestId} waiting for approval.");
        }

        if (request.RequestedByUserId == userId)
        {
            return Error.Rule("Ar.WriteOffSelfApproval", "Someone other than who asked for the write-off approves it.");
        }

        if (!approve)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Error.Validation("Ar.WriteOffReason", "Give the reason for turning the write-off down.");
            }

            request.Reject(userId, reason.Trim());
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<VatReliefWriteOff?>.Success(null);
        }

        Result<VatReliefWriteOff> written = await _ar.WriteOffWithVatReliefAsync(companyId, request.OpenItemId, date, request.BadDebtAccountCode, request.Reason, request.MinimumDaysOverdue, ct)
            .ConfigureAwait(false);
        if (written.IsFailure)
        {
            return written.Error;
        }

        request.Approve(userId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<VatReliefWriteOff?>.Success(written.Value);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingWriteOff>> PendingAsync(long companyId, CancellationToken ct = default)
        => await (from r in _db.Set<WriteOffRequest>().AsNoTracking()
                  join o in _db.OpenItems.AsNoTracking() on r.OpenItemId equals o.Id
                  join c in _db.Customers.AsNoTracking() on o.TraderId equals c.Id
                  where r.CompanyId == companyId && r.Status == WriteOffRequestStatus.Pending
                  orderby r.Id
                  select new PendingWriteOff(r.Id, c.Code, r.InvoiceNumber, r.Amount, r.Reason, r.RequestedByUserId)).ToListAsync(ct).ConfigureAwait(false);
}
