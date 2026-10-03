using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Period-end FX revaluation of open foreign-currency monetary accounts (brief §3.3, IAS 21). For each
/// monetary account restricted to a non-base currency it retranslates the foreign balance at the closing
/// rate and posts the unrealised difference to the FX gain/loss accounts, optionally auto-reversing in the
/// next period. Non-monetary items (equity, and by policy stock/fixed assets) are not revalued.
/// </summary>
public sealed class FxRevaluationService : IFxRevaluationService
{
    private const int Scale = 4;
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    public FxRevaluationService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public Task<Result<FxRevaluationResult>> RevalueAsync(long companyId, DateOnly asAtDate, bool autoReverse = true, CancellationToken ct = default)
        => _db.AtomicAsync(() => RevalueCoreAsync(companyId, asAtDate, autoReverse, ct), ct);

    // One unit of work: every posting it makes commits together or not at all (joining a caller's transaction).
    private async Task<Result<FxRevaluationResult>> RevalueCoreAsync(long companyId, DateOnly asAtDate, bool autoReverse, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Fx.CompanyNotFound", $"Company {companyId} not found.");
        }

        CurrencyCode baseCurrency = company.BaseCurrency;

        string? fxGainCode = await ControlAccountCodeAsync(companyId, ControlAccountType.FxGain, ct).ConfigureAwait(false);
        string? fxLossCode = await ControlAccountCodeAsync(companyId, ControlAccountType.FxLoss, ct).ConfigureAwait(false);
        if (fxGainCode is null || fxLossCode is null)
        {
            return Error.Rule("Fx.NoGainLossAccounts", "FX gain and/or FX loss control accounts are not configured.");
        }

        // Foreign monetary accounts to revalue.
        List<LedgerAccount> accounts = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.CurrencyRestriction != null && a.CurrencyRestriction != baseCurrency &&
                        (a.Type == AccountType.Asset || a.Type == AccountType.Liability))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<AccountRevaluation> revaluations = [];

        // A currency-restricted account only takes lines in its own currency, so each foreign currency gets its own
        // journal (and reversal); the runs for all currencies stand or fall together.
        SortedDictionary<string, (CurrencyCode Currency, List<JournalLineRequest> Lines, List<JournalLineRequest> Reversal)> byCurrency = new(StringComparer.Ordinal);

        foreach (LedgerAccount account in accounts)
        {
            CurrencyCode currency = account.CurrencyRestriction!.Value;

            (decimal foreignNet, decimal baseNet) = await BalancesAsync(companyId, account.AccountCode, asAtDate, ct).ConfigureAwait(false);
            if (foreignNet == 0m)
            {
                continue;
            }

            Result<decimal> rateResult = await ClosingRateAsync(currency, baseCurrency, asAtDate, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            decimal rate = rateResult.Value;
            decimal revaluedBase = decimal.Round(foreignNet * rate, Scale, MidpointRounding.ToEven);
            decimal diff = revaluedBase - baseNet;

            revaluations.Add(new AccountRevaluation(account.AccountCode, currency.Value, foreignNet, rate, baseNet, revaluedBase, diff));
            if (diff == 0m)
            {
                continue;
            }

            if (!byCurrency.TryGetValue(currency.Value, out var group))
            {
                group = (currency, [], []);
                byCurrency[currency.Value] = group;
            }

            string fxCode = diff > 0m ? fxGainCode : fxLossCode;
            JournalSide accountSide = diff > 0m ? JournalSide.Debit : JournalSide.Credit;
            JournalSide fxSide = diff > 0m ? JournalSide.Credit : JournalSide.Debit;
            decimal magnitude = Math.Abs(diff);

            group.Lines.Add(new JournalLineRequest { AccountCode = account.AccountCode, Side = accountSide, TransactionAmount = 0m, BaseAmount = magnitude, Narrative = "FX revaluation" });
            group.Lines.Add(new JournalLineRequest { AccountCode = fxCode, Side = fxSide, TransactionAmount = 0m, BaseAmount = magnitude, Narrative = "FX revaluation" });

            group.Reversal.Add(new JournalLineRequest { AccountCode = account.AccountCode, Side = fxSide, TransactionAmount = 0m, BaseAmount = magnitude, Narrative = "FX revaluation reversal" });
            group.Reversal.Add(new JournalLineRequest { AccountCode = fxCode, Side = accountSide, TransactionAmount = 0m, BaseAmount = magnitude, Narrative = "FX revaluation reversal" });
        }

        if (byCurrency.Count == 0)
        {
            return new FxRevaluationResult(asAtDate, revaluations, [], []);
        }

        List<string> journalNumbers = [];
        List<string> reversalNumbers = [];
        foreach ((CurrencyCode currency, List<JournalLineRequest> lines, List<JournalLineRequest> reversalLines) in byCurrency.Values)
        {
            Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = asAtDate,
                TransactionCurrency = currency,
                EnforceManualJournalRules = false,
                DocumentType = "FxRevaluation",
                Narrative = $"FX revaluation ({currency}) as at {asAtDate:yyyy-MM-dd}",
                Lines = lines,
            }, ct).ConfigureAwait(false);

            if (posting.IsFailure)
            {
                return posting.Error;
            }

            journalNumbers.Add(posting.Value.JournalNumber);
            if (!autoReverse)
            {
                continue;
            }

            Result<PostingReceipt> reversal = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = asAtDate.AddDays(1),
                TransactionCurrency = currency,
                EnforceManualJournalRules = false,
                DocumentType = "FxRevaluationReversal",
                Narrative = $"FX revaluation ({currency}) reversal of {asAtDate:yyyy-MM-dd}",
                Lines = reversalLines,
            }, ct).ConfigureAwait(false);

            if (reversal.IsFailure)
            {
                return reversal.Error;
            }

            reversalNumbers.Add(reversal.Value.JournalNumber);
        }
        return new FxRevaluationResult(asAtDate, revaluations, journalNumbers, reversalNumbers);
    }

    private async Task<string?> ControlAccountCodeAsync(long companyId, ControlAccountType type, CancellationToken ct) =>
        await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == type)
            .Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    private async Task<(decimal foreignNet, decimal baseNet)> BalancesAsync(long companyId, string accountCode, DateOnly asAtDate, CancellationToken ct)
    {
        var totals = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && l.AccountCode == accountCode && j.PostingDate <= asAtDate
            select new { l.TransactionDebit, l.TransactionCredit, l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);

        decimal foreignNet = totals.Sum(t => t.TransactionDebit) - totals.Sum(t => t.TransactionCredit);
        decimal baseNet = totals.Sum(t => t.BaseDebit) - totals.Sum(t => t.BaseCredit);
        return (foreignNet, baseNet);
    }

    private async Task<Result<decimal>> ClosingRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken ct)
    {
        decimal? rate = await ExchangeRateResolver.FindAsync(_db, from, to, ExchangeRateType.ClosingRate, date, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Fx.NoClosingRate", $"No closing rate from {from} to {to} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Value;
    }
}
