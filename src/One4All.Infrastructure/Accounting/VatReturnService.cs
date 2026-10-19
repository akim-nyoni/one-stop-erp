using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Settles a VAT return by clearing the VAT-output and VAT-input control accounts for a period and posting
/// the net to a VAT payable/refundable account, through the central posting engine. Each VAT control account
/// is zeroed by the opposite of its net movement over the period, so multi-rate charts (several output/input
/// accounts) are handled, and the net of the clearing lines determines the payable/refundable balance.
/// </summary>
public sealed class VatReturnService : IVatReturnService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The central posting engine.</param>
    /// <param name="tax">Works out the return on the company's basis (cash accounting or the tax point).</param>
    public VatReturnService(One4AllDbContext db, IPostingEngine postingEngine, ITaxComplianceService? tax = null)
    {
        _db = db;
        _postingEngine = postingEngine;
        _tax = tax;
    }

    private readonly ITaxComplianceService? _tax;

    /// <inheritdoc />
    public async Task<Result<VatSettlementResult>> SettleAsync(SettleVatRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VatPayableAccountCode))
        {
            return Error.Validation("Vat.PayableAccount", "The VAT payable account is required.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);

        // A period is filed once: a second settlement over any of its days is refused.
        if (await _db.VatReturnFilings.AnyAsync(f => f.CompanyId == request.CompanyId && f.FromDate <= request.ToDate && f.ToDate >= request.FromDate, ct).ConfigureAwait(false))
        {
            return Error.Rule("Vat.PeriodFiled", $"A VAT return already covers part of {request.FromDate:yyyy-MM-dd} to {request.ToDate:yyyy-MM-dd}.");
        }

        List<LedgerAccount> vatAccounts = await _db.Accounts
            .Where(a => a.CompanyId == request.CompanyId
                && (a.ControlAccountType == ControlAccountType.VatOutput || a.ControlAccountType == ControlAccountType.VatInput))
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, (decimal debit, decimal credit)> movements = await MovementByAccountAsync(request.CompanyId, request.FromDate, request.ToDate, ct).ConfigureAwait(false);

        decimal outputTax = 0m;
        decimal inputTax = 0m;
        List<JournalLineRequest> lines = [];
        foreach (LedgerAccount account in vatAccounts)
        {
            (decimal debit, decimal credit) = movements.GetValueOrDefault(account.AccountCode, (0m, 0m));
            decimal net = debit - credit; // debit-positive balance to clear
            if (net == 0m)
            {
                continue;
            }

            // Clear the account with the opposite side.
            lines.Add(new JournalLineRequest
            {
                AccountCode = account.AccountCode,
                Side = net > 0m ? JournalSide.Credit : JournalSide.Debit,
                TransactionAmount = Math.Abs(net),
            });

            if (account.ControlAccountType == ControlAccountType.VatOutput)
            {
                outputTax += -net; // output is a credit balance
            }
            else
            {
                inputTax += net; // input is a debit balance
            }
        }

        // On cash accounting or the tax point the return is not the ledger's movement: the difference is VAT not yet due (held back, or released from earlier).
        decimal deferred = 0m;
        if (company.TaxPointBasis != TaxPointBasis.InvoiceDate && _tax is not null)
        {
            BasisVatReturn basis = await _tax.VatReturnAsync(request.CompanyId, request.FromDate, request.ToDate, company.TaxPointBasis, ct).ConfigureAwait(false);
            deferred = (outputTax - basis.OutputTax) - (inputTax - basis.InputTax);
            (outputTax, inputTax) = (basis.OutputTax, basis.InputTax);
            if (deferred != 0m)
            {
                if (string.IsNullOrWhiteSpace(request.VatNotYetDueAccountCode))
                {
                    return Error.Validation("Vat.NotYetDueAccount", $"On {company.TaxPointBasis} accounting {Math.Abs(deferred):N2} of VAT is not due in this period: give the VAT-not-yet-due account.");
                }

                lines.Add(new JournalLineRequest
                {
                    AccountCode = request.VatNotYetDueAccountCode.Trim(), Side = deferred > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(deferred),
                    Narrative = deferred > 0m ? "VAT not yet due, held back" : "VAT now due, released",
                });
            }
        }

        if (lines.Count == 0)
        {
            _db.VatReturnFilings.Add(new VatReturnFiling(request.CompanyId, request.FromDate, request.ToDate, company.TaxPointBasis, request.DocumentNumber) { FiledByUserId = request.FiledByUserId });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new VatSettlementResult(0m, 0m, 0m, null); // nothing to settle
        }

        // Balance the clearing lines with the VAT payable/refundable account.
        decimal clearingNet = lines.Sum(l => l.Side == JournalSide.Debit ? l.TransactionAmount : -l.TransactionAmount);
        if (clearingNet != 0m)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = request.VatPayableAccountCode,
                Side = clearingNet > 0m ? JournalSide.Credit : JournalSide.Debit,
                TransactionAmount = Math.Abs(clearingNet),
            });
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "GL",
            PostingDate = request.ToDate,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "VatReturn",
            DocumentNumber = request.DocumentNumber,
            Narrative = $"VAT return {request.FromDate:yyyy-MM-dd} to {request.ToDate:yyyy-MM-dd}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        _db.VatReturnFilings.Add(new VatReturnFiling(request.CompanyId, request.FromDate, request.ToDate, company.TaxPointBasis, request.DocumentNumber)
        {
            OutputTax = outputTax, InputTax = inputTax, Deferred = deferred, JournalNumber = posting.Value.JournalNumber, FiledByUserId = request.FiledByUserId,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new VatSettlementResult(outputTax, inputTax, outputTax - inputTax, posting.Value.JournalNumber, deferred);
    }

    private async Task<Dictionary<string, (decimal debit, decimal credit)>> MovementByAccountAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var totals = await _db.Set<JournalLine>()
            .Where(l => _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted &&
                j.PostingDate >= from && j.PostingDate <= to))
            .GroupBy(l => l.AccountCode)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return totals.ToDictionary(t => t.Code, t => (t.Debit, t.Credit));
    }
}
