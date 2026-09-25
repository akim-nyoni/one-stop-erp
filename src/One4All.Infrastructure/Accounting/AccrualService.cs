using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Posts auto-reversing accrual and prepayment journals through the central posting engine, reusing
/// <see cref="IPostingEngine.ReverseAsync"/> for the next-period reversal so the pair is linked and nets to
/// zero exactly like any other reversal.
/// </summary>
public sealed class AccrualService : IAccrualService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public AccrualService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<ReversingJournalResult>> PostAccrualAsync(long companyId, DateOnly date, DateOnly reversalDate, string expenseAccountCode, string accrualAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default)
        => PostReversingAsync(companyId, date, reversalDate, expenseAccountCode, accrualAccountCode, amount, "GL-ACCR", documentNumber, narrative, ct);

    /// <inheritdoc />
    public Task<Result<ReversingJournalResult>> PostPrepaymentAsync(long companyId, DateOnly date, DateOnly reversalDate, string prepaymentAccountCode, string expenseAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default)
        => PostReversingAsync(companyId, date, reversalDate, prepaymentAccountCode, expenseAccountCode, amount, "GL-PREP", documentNumber, narrative, ct);

    private async Task<Result<ReversingJournalResult>> PostReversingAsync(long companyId, DateOnly date, DateOnly reversalDate, string debitAccountCode, string creditAccountCode, decimal amount, string documentType, string documentNumber, string narrative, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("GL.ReversingNonPositive", "The amount must be positive.");
        }

        if (reversalDate <= date)
        {
            return Error.Rule("GL.ReversalNotAfter", "The reversal date must be after the posting date.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        Result<PostingReceipt> reversal = await _posting.ReverseAsync(companyId, posted.Value.JournalId, reversalDate, $"Auto-reversal of {narrative}", ct).ConfigureAwait(false);
        return reversal.IsFailure
            ? reversal.Error
            : new ReversingJournalResult(posted.Value.JournalNumber, reversal.Value.JournalNumber);
    }
}
