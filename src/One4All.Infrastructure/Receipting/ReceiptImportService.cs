using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using TradingReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// Bulk / file-based receipting (brief §RCP-8). Imports a payment file, matches each line to a customer by
/// the account code appearing in the payment reference, receipts matched lines through the AR ledger
/// (Dr bank / Cr debtor control, allocated oldest-first) and parks unmatched money in the suspense
/// control (Dr bank / Cr suspense — never a guessed account). Re-importing the same file (by hash) fails.
/// </summary>
public sealed class ReceiptImportService : IReceiptImportService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IAllocationService _allocation;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger (matched receipts).</param>
    /// <param name="allocation">The allocation service.</param>
    public ReceiptImportService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IAllocationService allocation)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _allocation = allocation;
    }

    /// <inheritdoc />
    public Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => ImportCoreAsync(request, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ImportResult>> ImportCoreAsync(ImportRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Error.Validation("Rcp.EmptyImport", "The import file has no lines.");
        }

        bool duplicate = await _db.Set<ImportBatch>()
            .AnyAsync(b => b.CompanyId == request.CompanyId && b.FileHash == request.FileHash, ct).ConfigureAwait(false);
        if (duplicate)
        {
            return Error.Conflict("Rcp.DuplicateImport", $"A file with hash {request.FileHash} has already been imported.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        // Candidate accounts for matching (code → id). Councils quote the account in the reference, badly.
        var customerRows = await _db.Set<Customer>()
            .Where(c => c.CompanyId == request.CompanyId)
            .Select(c => new { c.Id, c.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        List<(long Id, string Code)> customers = customerRows.Select(x => (x.Id, x.Code)).ToList();

        ImportBatch batch = new(request.CompanyId, request.FileReference, request.FileHash, request.Source, request.Date, request.BankAccountCode);
        _db.Set<ImportBatch>().Add(batch);

        decimal matchedAmount = 0m;
        decimal unmatchedAmount = 0m;
        string hashPrefix = request.FileHash.Length <= 8 ? request.FileHash : request.FileHash[..8];
        int index = 0;

        foreach (ImportLineRequest lineRequest in request.Lines)
        {
            index++;
            string documentNumber = $"IMP-{hashPrefix}-{index}";
            ImportLine line = batch.AddLine(lineRequest.PayerReference, lineRequest.PayerName, lineRequest.Amount, lineRequest.ValueDate);
            long? matchedCustomerId = MatchCustomer(lineRequest.PayerReference, customers);

            if (matchedCustomerId is { } customerId)
            {
                Result<TradingPostingResult> receipt = await _salesLedger.PostReceiptAsync(new TradingReceiptRequest
                {
                    CompanyId = request.CompanyId,
                    CustomerId = customerId,
                    DocumentNumber = documentNumber,
                    ReceiptDate = lineRequest.ValueDate,
                    Currency = company.BaseCurrency,
                    Amount = lineRequest.Amount,
                    BankAccountCode = request.BankAccountCode,
                    Narrative = $"Imported payment — {lineRequest.PayerName}",
                }, ct).ConfigureAwait(false);

                if (receipt.IsFailure)
                {
                    return receipt.Error;
                }

                line.MatchTo(customerId);
                line.OpenItemId = receipt.Value.OpenItemId;
                await AllocateOldestFirstAsync(request.CompanyId, customerId, receipt.Value.OpenItemId, lineRequest.Amount, lineRequest.ValueDate, ct).ConfigureAwait(false);
                matchedAmount += lineRequest.Amount;
            }
            else
            {
                // Unmatched → Dr bank / Cr suspense control (never a guessed account).
                Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
                {
                    CompanyId = request.CompanyId,
                    SourceModule = "GL",
                    PostingDate = lineRequest.ValueDate,
                    TransactionCurrency = company.BaseCurrency,
                    DocumentType = "IMPORT-SUS",
                    DocumentNumber = documentNumber,
                    Narrative = $"Unmatched import — {lineRequest.PayerName} ({lineRequest.PayerReference})",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = request.BankAccountCode, Side = JournalSide.Debit, TransactionAmount = lineRequest.Amount },
                        new JournalLineRequest { AccountCode = request.SuspenseAccountCode, Side = JournalSide.Credit, TransactionAmount = lineRequest.Amount },
                    ],
                }, ct).ConfigureAwait(false);

                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                unmatchedAmount += lineRequest.Amount;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ImportResult(batch.Id, request.Lines.Count, batch.MatchedCount, batch.UnmatchedCount, matchedAmount, unmatchedAmount);
    }

    private static long? MatchCustomer(string payerReference, List<(long Id, string Code)> customers)
    {
        string reference = payerReference.ToUpperInvariant();
        List<long> matches = customers
            .Where(c => !string.IsNullOrEmpty(c.Code) && reference.Contains(c.Code, StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToList();

        // Match only when exactly one account is identified; ambiguity goes to the suspense workbench.
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task AllocateOldestFirstAsync(long companyId, long customerId, long creditItemId, decimal amount, DateOnly date, CancellationToken ct)
    {
        List<OpenItem> debits = await _db.Set<OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal remaining = amount;
        foreach (OpenItem debit in debits)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, debit.OutstandingTransaction);
            Result<AllocationResult> result = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, debit.Id, creditItemId, take, date, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                break;
            }

            remaining -= take;
        }
    }
}
