using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Petty cash on the imprest system: disburses expenses from the float and reimburses the float back to its
/// set amount by drawing the spent total from the bank, all through the central posting engine.
/// </summary>
public sealed class PettyCashService : IPettyCashService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The central posting engine.</param>
    public PettyCashService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public Task<Result<string>> DisburseAsync(long companyId, decimal amount, DateOnly date, string expenseAccountCode, string pettyCashAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => DisburseCoreAsync(companyId, amount, date, expenseAccountCode, pettyCashAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> DisburseCoreAsync(long companyId, decimal amount, DateOnly date, string expenseAccountCode, string pettyCashAccountCode, string documentNumber, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Validation("PettyCash.Amount", "A disbursement amount must be positive.");
        }

        Result<PostingReceipt> posting = await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash disbursement {documentNumber}",
            (expenseAccountCode, JournalSide.Debit, amount),
            (pettyCashAccountCode, JournalSide.Credit, amount)).ConfigureAwait(false);

        return posting.IsSuccess ? posting.Value.JournalNumber : posting.Error;
    }

    /// <inheritdoc />
    public Task<Result<ImprestResult>> ReimburseToFloatAsync(long companyId, DateOnly date, string pettyCashAccountCode, string bankAccountCode, decimal floatAmount, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReimburseToFloatCoreAsync(companyId, date, pettyCashAccountCode, bankAccountCode, floatAmount, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ImprestResult>> ReimburseToFloatCoreAsync(long companyId, DateOnly date, string pettyCashAccountCode, string bankAccountCode, decimal floatAmount, string documentNumber, CancellationToken ct)
    {
        if (floatAmount <= 0m)
        {
            return Error.Validation("PettyCash.Float", "The float amount must be positive.");
        }

        decimal balance = await BalanceAsync(companyId, pettyCashAccountCode, date, ct).ConfigureAwait(false);
        decimal shortfall = decimal.Round(floatAmount - balance, 2, MidpointRounding.AwayFromZero);
        if (shortfall <= 0m)
        {
            return new ImprestResult(0m, balance, null); // float already full
        }

        Result<PostingReceipt> posting = await PostAsync(companyId, date, "CB", documentNumber, $"Petty cash reimbursement {documentNumber}",
            (pettyCashAccountCode, JournalSide.Debit, shortfall),
            (bankAccountCode, JournalSide.Credit, shortfall)).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        return new ImprestResult(shortfall, balance + shortfall, posting.Value.JournalNumber);
    }

    private async Task<decimal> BalanceAsync(long companyId, string accountCode, DateOnly asAt, CancellationToken ct)
    {
        var totals = await _db.Set<JournalLine>()
            .Where(l => l.AccountCode == accountCode && _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAt))
            .GroupBy(l => 1)
            .Select(g => new { Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return totals is null ? 0m : totals.Debit - totals.Credit;
    }

    private async Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string module, string documentNumber, string narrative, params (string Account, JournalSide Side, decimal Amount)[] lines)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId).ConfigureAwait(false);
        return await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = module,
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PettyCash",
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount }).ToList(),
        }).ConfigureAwait(false);
    }
}
