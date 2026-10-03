using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Maker-checker approval of manual journals over the posting engine's held (deferred) journals.</summary>
public sealed class JournalApprovalService : IJournalApprovalService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public JournalApprovalService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> SubmitAsync(JournalRequest request, long preparedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        JournalRequest held = new()
        {
            CompanyId = request.CompanyId,
            SourceModule = request.SourceModule,
            PostingDate = request.PostingDate,
            TransactionCurrency = request.TransactionCurrency,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            Reference = request.Reference,
            Narrative = request.Narrative,
            EnforceManualJournalRules = true,
            Deferred = true,
            SubmittedByUserId = preparedByUserId,
            IdempotencyKey = request.IdempotencyKey,
            Lines = request.Lines,
        };
        return _engine.PostAsync(held, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalAwaitingApproval>> AwaitingApprovalAsync(long companyId, CancellationToken ct = default)
        => await _db.Journals.AsNoTracking()
            .Where(j => j.CompanyId == companyId && j.AwaitingApproval)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.Id)
            .Select(j => new JournalAwaitingApproval(j.Id, j.JournalNumber, j.PostingDate, j.Narrative, j.Lines.Sum(l => l.BaseDebit), j.PreparedByUserId ?? 0))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> ApproveAsync(long companyId, long journalId, long approverUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, journalId, approverUserId, ct), ct);

    // One unit: the approval is kept only if the journal then posts.
    private async Task<Result<PostingReceipt>> ApproveCoreAsync(long companyId, long journalId, long approverUserId, CancellationToken ct)
    {
        Result<JournalEntry> held = await FindHeldAsync(companyId, journalId, ct).ConfigureAwait(false);
        if (held.IsFailure)
        {
            return held.Error;
        }

        if (!held.Value.Approve(approverUserId))
        {
            return Error.Rule("Gl.SelfApproval", $"Journal {held.Value.JournalNumber} was prepared by this user; a different user must approve it.");
        }

        return await _engine.ReleaseDeferredAsync(companyId, journalId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> RejectAsync(long companyId, long journalId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => RejectCoreAsync(companyId, journalId, reason, ct), ct);

    private async Task<Result> RejectCoreAsync(long companyId, long journalId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Gl.ReasonRequired", "Rejecting a journal requires a reason."));
        }

        Result<JournalEntry> held = await FindHeldAsync(companyId, journalId, ct).ConfigureAwait(false);
        if (held.IsFailure)
        {
            return Result.Failure(held.Error);
        }

        held.Value.Reject(reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<JournalEntry>> FindHeldAsync(long companyId, long journalId, CancellationToken ct)
    {
        JournalEntry? journal = await _db.Journals.FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == companyId, ct).ConfigureAwait(false);
        if (journal is null)
        {
            return Error.NotFound("Gl.JournalNotFound", $"Journal {journalId} not found.");
        }

        if (!journal.AwaitingApproval)
        {
            return Error.Rule("Gl.NotAwaitingApproval", $"Journal {journal.JournalNumber} is {journal.Status} and not awaiting approval.");
        }

        return journal;
    }
}
