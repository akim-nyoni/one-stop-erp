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
    /// <param name="workflow">The approval workflow, when the company defines one for journals.</param>
    public JournalApprovalService(One4AllDbContext db, IPostingEngine engine, Lazy<Application.Abstractions.Workflow.IWorkflowService>? workflow = null)
    {
        _db = db;
        _engine = engine;
        _workflow = workflow;
    }

    private readonly Lazy<Application.Abstractions.Workflow.IWorkflowService>? _workflow;

    /// <inheritdoc />
    public Task<Result<PostingReceipt>> SubmitAsync(JournalRequest request, long preparedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(async () =>
        {
            Result<PostingReceipt> held = await HoldAsync(request, preparedByUserId, ct).ConfigureAwait(false);
            if (held.IsFailure || !held.Value.Deferred || _workflow is null
                || !await _db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == request.CompanyId && d.DocumentType == Application.Abstractions.Workflow.WorkflowDocumentTypes.Journal && d.IsActive, ct).ConfigureAwait(false))
            {
                return held;
            }

            // The company's journal workflow governs the approval from here.
            string reference = held.Value.JournalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (await _db.WorkflowInstances.AnyAsync(i => i.CompanyId == request.CompanyId && i.DocumentType == Application.Abstractions.Workflow.WorkflowDocumentTypes.Journal && i.DocumentReference == reference, ct)
                    .ConfigureAwait(false))
            {
                return held;
            }

            IReadOnlyDictionary<string, string> attributes = await Workflow.WorkflowGate.AttributesAsync(_db, preparedByUserId,
            [
                ("Currency", request.TransactionCurrency.Value), ("Department", request.Lines.Select(l => l.Department).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))),
                ("CostCentre", request.Lines.Select(l => l.CostCentre).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))),
                ("Project", request.Lines.Select(l => l.Project).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))),
            ], ct).ConfigureAwait(false);
            Result<Application.Abstractions.Workflow.WorkflowStartResult> started = await _workflow.Value.StartAsync(request.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.Journal,
                reference, held.Value.TotalBase, preparedByUserId, attributes, ct).ConfigureAwait(false);
            return started.IsFailure ? started.Error : held;
        }, ct);
    }

    private Task<Result<PostingReceipt>> HoldAsync(JournalRequest request, long preparedByUserId, CancellationToken ct)
    {
        // A manual journal is a general ledger entry whatever the caller says: claiming another module ("AR") would let it post into a period
        // closed for the ledger but still open for that module.
        JournalRequest held = new()
        {
            CompanyId = request.CompanyId,
            SourceModule = "GL",
            PostingDate = request.PostingDate,
            TransactionCurrency = request.TransactionCurrency,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            Reference = request.Reference,
            ReverseOn = request.ReverseOn,
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
    public async Task<Result<PostingReceipt>> ApproveAsync(long companyId, long journalId, long approverUserId, CancellationToken ct = default)
        => await GovernedAsync(companyId, journalId, ct).ConfigureAwait(false) is Error governed ? governed : await ApproveFromWorkflowAsync(companyId, journalId, approverUserId, ct).ConfigureAwait(false);

    /// <summary>Whether the company has the journal held for approval.</summary>
    internal Task<bool> IsHeldAsync(long companyId, long journalId, CancellationToken ct) => _db.Journals.AnyAsync(j => j.Id == journalId && j.CompanyId == companyId && j.AwaitingApproval, ct);

    /// <summary>Approves and posts without the workflow check (the workflow itself calls this when it ends approved).</summary>
    internal Task<Result<PostingReceipt>> ApproveFromWorkflowAsync(long companyId, long journalId, long approverUserId, CancellationToken ct)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, journalId, approverUserId, ct), ct);

    // A journal under a workflow in progress is approved or rejected only through it.
    private async Task<Error?> GovernedAsync(long companyId, long journalId, CancellationToken ct)
    {
        string reference = journalId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await _db.WorkflowInstances.AnyAsync(i => i.CompanyId == companyId && i.DocumentType == Application.Abstractions.Workflow.WorkflowDocumentTypes.Journal
                && i.DocumentReference == reference && i.Status == Core.Workflow.WorkflowStatus.InProgress, ct).ConfigureAwait(false)
            ? Error.Rule("Workflow.Governs", "This journal is going through its approval workflow; approve or reject it there.")
            : null;
    }

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

        Result<PostingReceipt> posted = await _engine.ReleaseDeferredAsync(companyId, journalId, ct).ConfigureAwait(false);
        if (posted.IsFailure || held.Value.ReverseOn is not DateOnly reverseOn)
        {
            return posted;
        }

        // An accrual reverses itself on its reverse-on date, in the same unit as its posting.
        Result<PostingReceipt> reversed = await _engine.ReverseAsync(companyId, journalId, reverseOn, $"Auto-reversal of {held.Value.JournalNumber}", ct).ConfigureAwait(false);
        return reversed.IsFailure ? reversed.Error : posted;
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long journalId, string reason, CancellationToken ct = default)
        => await GovernedAsync(companyId, journalId, ct).ConfigureAwait(false) is Error governed ? Result.Failure(governed) : await RejectFromWorkflowAsync(companyId, journalId, reason, ct).ConfigureAwait(false);

    /// <summary>Rejects without the workflow check (the workflow itself calls this when it ends rejected or returned).</summary>
    internal Task<Result> RejectFromWorkflowAsync(long companyId, long journalId, string reason, CancellationToken ct)
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

/// <summary>Applies the end of a journal's approval workflow: approved → the journal is approved by the last approver and posts; rejected or returned → rejected.</summary>
internal sealed class JournalWorkflowHandler : Application.Abstractions.Workflow.IWorkflowDocumentHandler
{
    private readonly JournalApprovalService _journals;

    public JournalWorkflowHandler(JournalApprovalService journals) => _journals = journals;

    public string DocumentType => Application.Abstractions.Workflow.WorkflowDocumentTypes.Journal;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        // A workflow started on its own (its reference is not one of this company's held journals) has nothing to apply.
        if (!long.TryParse(documentReference, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id) || !await _journals.IsHeldAsync(companyId, id, ct).ConfigureAwait(false))
        {
            return Result.Success();
        }

        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            return await _journals.RejectFromWorkflowAsync(companyId, id, string.IsNullOrWhiteSpace(comment) ? $"{outcome} in the approval workflow" : comment!, ct).ConfigureAwait(false);
        }

        Result<PostingReceipt> posted = await _journals.ApproveFromWorkflowAsync(companyId, id, decidedByUserId, ct).ConfigureAwait(false);
        return posted.IsFailure ? Result.Failure(posted.Error) : Result.Success();
    }
}
