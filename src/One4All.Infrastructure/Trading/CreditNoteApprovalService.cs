using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Customer credit notes held for approval until their workflow ends (brief §7).</summary>
public sealed class CreditNoteApprovalService : ICreditNoteApprovalService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;
    private readonly Lazy<IWorkflowService>? _workflow;
    private readonly Application.Abstractions.Numbering.INumberSequenceService? _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">Posts the credit note once approved.</param>
    /// <param name="workflow">The approval workflow.</param>
    /// <param name="numbers">Numbers a credit note held for approval without one (the "SCN" sequence).</param>
    public CreditNoteApprovalService(One4AllDbContext db, ISalesLedgerService ar, Lazy<IWorkflowService>? workflow = null, Application.Abstractions.Numbering.INumberSequenceService? numbers = null)
    {
        _db = db;
        _ar = ar;
        _workflow = workflow;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<CreditNoteSubmission>> SubmitAsync(SalesCreditNoteRequest request, long submittedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (_workflow is null || !await _db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == request.CompanyId && d.DocumentType == WorkflowDocumentTypes.CreditNote && d.IsActive, ct).ConfigureAwait(false))
            {
                Result<TradingPostingResult> posted = await _ar.PostCreditNoteAsync(request, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                string postedNumber = await _db.OpenItems.Where(i => i.Id == posted.Value.OpenItemId).Select(i => i.DocumentNumber).FirstAsync(ct).ConfigureAwait(false);
                return Result<CreditNoteSubmission>.Success(new CreditNoteSubmission(true, posted.Value.JournalNumber) { DocumentNumber = postedNumber });
            }

            string number = (request.DocumentNumber ?? string.Empty).Trim();
            if (number.Length == 0 && _numbers is not null && request.NetAmount > 0m)
            {
                // A credit note held for approval takes its serial number now: the workflow is keyed on it.
                Result<string> allocated = await _numbers.AllocateAsync(request.CompanyId, TradingService.CreditNoteSequence, request.DocumentDate, ct: ct).ConfigureAwait(false);
                if (allocated.IsFailure)
                {
                    return allocated.Error;
                }

                number = allocated.Value;
            }

            if (number.Length == 0 || request.NetAmount <= 0m || string.IsNullOrWhiteSpace(request.SalesAccountCode))
            {
                return Error.Validation("Ar.CreditNote", "A credit note needs a number, a positive amount and the revenue account it reverses.");
            }

            if (await _db.Set<PendingCreditNote>().AnyAsync(p => p.CompanyId == request.CompanyId && p.DocumentNumber == number && p.Status != PendingCreditNoteStatus.Rejected, ct).ConfigureAwait(false)
                || await _db.OpenItems.AnyAsync(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == number, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Ar.CreditNoteExists", $"Credit note {number} already exists.");
            }

            if (!await _db.Customers.AnyAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
            }

            PendingCreditNote held = new(request.CompanyId, request.CustomerId, number, request.DocumentDate, request.Currency.Value, request.NetAmount, request.TaxAmount, request.SalesAccountCode.Trim(),
                request.TaxAccountCode, request.Narrative, request.ReasonCode, submittedByUserId)
            {
                TaxCode = string.IsNullOrWhiteSpace(request.TaxCode) ? null : request.TaxCode.Trim().ToUpperInvariant(),
                OriginalInvoiceNumber = string.IsNullOrWhiteSpace(request.OriginalInvoiceNumber) ? null : request.OriginalInvoiceNumber.Trim(),
            };
            _db.Set<PendingCreditNote>().Add(held);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, request.CompanyId, WorkflowDocumentTypes.CreditNote, number, request.NetAmount + request.TaxAmount,
                submittedByUserId,
                [
                    ("Currency", request.Currency.Value), ("Reason", request.ReasonCode),
                    ("Customer", await _db.Customers.Where(c => c.Id == request.CustomerId).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
                ], ct).ConfigureAwait(false);
            return governed.IsFailure ? governed.Error : Result<CreditNoteSubmission>.Success(new CreditNoteSubmission(false, null) { DocumentNumber = number });
        }, ct);

    /// <summary>Applies the end of a credit note's workflow: approved → posted; otherwise rejected (the ledger untouched).</summary>
    internal async Task<Result> ConcludeAsync(long companyId, string number, bool approved, CancellationToken ct)
    {
        PendingCreditNote? held = await _db.Set<PendingCreditNote>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.DocumentNumber == number && p.Status == PendingCreditNoteStatus.Pending, ct)
            .ConfigureAwait(false);
        if (held is null)
        {
            return Result.Success();
        }

        if (!approved)
        {
            held.Reject();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }

        Result<TradingPostingResult> posted = await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = held.CustomerId, DocumentNumber = held.DocumentNumber, DocumentDate = held.DocumentDate, Currency = CurrencyCode.Parse(held.Currency),
            NetAmount = held.NetAmount, TaxAmount = held.TaxAmount, SalesAccountCode = held.SalesAccountCode, TaxAccountCode = held.TaxAccountCode, Narrative = held.Narrative, ReasonCode = held.ReasonCode,
            TaxCode = held.TaxCode, OriginalInvoiceNumber = held.OriginalInvoiceNumber,
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return Result.Failure(posted.Error);
        }

        held.MarkPosted(posted.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Applies the end of a credit note's workflow.</summary>
internal sealed class CreditNoteWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly CreditNoteApprovalService _credits;

    public CreditNoteWorkflowHandler(CreditNoteApprovalService credits) => _credits = credits;

    public string DocumentType => WorkflowDocumentTypes.CreditNote;

    public Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
        => _credits.ConcludeAsync(companyId, documentReference, outcome == Core.Workflow.WorkflowStatus.Approved, ct);
}
