using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>Pickups, paid-outs and float top-ups on a till session (brief §4.1).</summary>
public sealed class TillCashService : ITillCashService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public TillCashService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<string>> RecordAsync(TillCashRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordCoreAsync(request, ct), ct);

    private async Task<Result<string>> RecordCoreAsync(TillCashRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0m || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.CashAccountCode) || string.IsNullOrWhiteSpace(request.OtherAccountCode))
        {
            return Error.Validation("Pos.TillCashInvalid", "A till cash movement needs a positive amount, a reason and both accounts.");
        }

        if (request.Kind == TillCashKind.Refund)
        {
            return Error.Rule("Pos.RefundThroughService", "Refunds from the drawer are recorded by the refund itself.");
        }

        if (request.Kind == TillCashKind.PaidOut && (request.AuthorisedByUserId is null || request.AuthorisedByUserId == request.UserId))
        {
            return Error.Rule("Pos.AuthorisationRequired", "A paid-out needs a supervisor other than the cashier to authorise it.");
        }

        TillSession? session = await _db.Set<TillSession>().FirstOrDefaultAsync(s => s.Id == request.TillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Pos.SessionNotFound", $"Till session {request.TillSessionId} not found.");
        }

        if (session.Status != TillSessionStatus.Open)
        {
            return Error.Rule("Pos.SessionClosed", "The till session is already closed.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == session.CompanyId, ct).ConfigureAwait(false);
        int sequence = await _db.TillCashMovements.CountAsync(m => m.TillSessionId == session.Id, ct).ConfigureAwait(false) + 1;
        bool into = request.Kind == TillCashKind.FloatTopUp;
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = session.CompanyId, SourceModule = "POS", PostingDate = request.Date, TransactionCurrency = company.BaseCurrency, EnforceManualJournalRules = false, DocumentType = "PosCash",
            DocumentNumber = $"TILL-{session.Id}-{sequence}", Narrative = $"{request.Kind} on till session {session.Id}: {request.Reason.Trim()}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.CashAccountCode, Side = into ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = request.Amount },
                new JournalLineRequest { AccountCode = request.OtherAccountCode, Side = into ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = request.Amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.TillCashMovements.Add(new TillCashMovement(session.CompanyId, session.Id, request.Kind, request.Amount, request.OtherAccountCode, request.Reason.Trim(), request.UserId,
            request.AuthorisedByUserId, request.Date, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }
}
