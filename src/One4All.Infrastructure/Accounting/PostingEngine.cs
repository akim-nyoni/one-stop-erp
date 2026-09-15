using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// The single, non-bypassable posting engine (brief §3.2). Validates and posts one balanced journal
/// inside one <see cref="One4AllDbContext.SaveChangesAsync"/> transaction, so the journal, its gapless
/// number and the period-balance updates commit together or not at all. The audit trail is written by
/// the change interceptor in the same transaction.
/// </summary>
public sealed class PostingEngine : IPostingEngine
{
    private const string JournalDocumentType = "GLJournal";
    private const decimal BalanceTolerance = 0m;

    private readonly One4AllDbContext _db;

    /// <summary>Creates the engine.</summary>
    /// <param name="db">The database context.</param>
    public PostingEngine(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<PostingReceipt>> PostAsync(JournalRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
        {
            return Error.Validation("Posting.NoLines", "A journal must have at least one line.");
        }

        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Posting.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        CurrencyCode baseCurrency = company.BaseCurrency;

        // Resolve the open period for the posting module.
        Result<(FiscalYear year, FiscalPeriod period)> periodResult =
            await ResolvePeriodAsync(request.CompanyId, request.SourceModule, request.PostingDate, company.AllowFuturePeriodPosting, ct)
                .ConfigureAwait(false);
        if (periodResult.IsFailure)
        {
            return periodResult.Error;
        }

        (FiscalYear fiscalYear, FiscalPeriod fiscalPeriod) = periodResult.Value;

        // Build and validate the lines.
        JournalEntry journal = new(request.CompanyId, request.SourceModule, request.PostingDate, baseCurrency, request.TransactionCurrency);
        journal.DocumentType = request.DocumentType;
        journal.DocumentNumber = request.DocumentNumber;
        journal.Reference = request.Reference;
        journal.Narrative = request.Narrative;

        Dictionary<string, (decimal debit, decimal credit)> perCurrency = new(StringComparer.Ordinal);
        decimal totalBaseDebit = 0m;
        decimal totalBaseCredit = 0m;

        foreach (JournalLineRequest lineRequest in request.Lines)
        {
            Result<JournalLine> lineResult = await BuildLineAsync(request, lineRequest, baseCurrency, ct).ConfigureAwait(false);
            if (lineResult.IsFailure)
            {
                return lineResult.Error;
            }

            JournalLine line = lineResult.Value;
            journal.AddLine(line);

            totalBaseDebit += line.BaseDebit;
            totalBaseCredit += line.BaseCredit;

            string cur = line.TransactionCurrency.Value;
            (decimal debit, decimal credit) running = perCurrency.GetValueOrDefault(cur);
            perCurrency[cur] = (running.debit + line.TransactionDebit, running.credit + line.TransactionCredit);
        }

        // Rule 1: Dr = Cr in base currency.
        if (Math.Abs(totalBaseDebit - totalBaseCredit) > BalanceTolerance)
        {
            return Error.Rule("Posting.OutOfBalance",
                $"Journal does not balance in base currency ({baseCurrency}): debits {totalBaseDebit:0.0000} vs credits {totalBaseCredit:0.0000}.");
        }

        // Rule 1 (continued): Dr = Cr per transaction currency.
        foreach ((string currency, (decimal debit, decimal credit)) in perCurrency)
        {
            if (Math.Abs(debit - credit) > BalanceTolerance)
            {
                return Error.Rule("Posting.OutOfBalanceCurrency",
                    $"Journal does not balance in {currency}: debits {debit:0.0000} vs credits {credit:0.0000}.");
            }
        }

        // Allocate the gapless journal number (increment persists in this transaction).
        string journalNumber = await AllocateJournalNumberAsync(request.CompanyId, request.PostingDate.Year, ct).ConfigureAwait(false);

        DateTimeOffset entryDate = DateTimeOffset.UtcNow;
        if (request.Deferred)
        {
            journal.MarkDeferred(journalNumber, fiscalYear.Id, fiscalPeriod.Id, entryDate);
        }
        else
        {
            journal.MarkPosted(journalNumber, fiscalYear.Id, fiscalPeriod.Id, entryDate);
            await UpdatePeriodBalancesAsync(request.CompanyId, fiscalYear.Id, fiscalPeriod.Id, journal, ct).ConfigureAwait(false);
        }

        _db.Journals.Add(journal);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new PostingReceipt(journal.Id, journalNumber, request.PostingDate, totalBaseDebit, request.Deferred);
    }

    private async Task<Result<JournalLine>> BuildLineAsync(JournalRequest request, JournalLineRequest lineRequest, CurrencyCode baseCurrency, CancellationToken ct)
    {
        AccountKey key = lineRequest.ToKey();
        LedgerAccount? account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.AccountCode == key.AccountCode, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return Error.NotFound("Posting.AccountNotFound", $"Account '{key.AccountCode}' does not exist.");
        }

        if (!account.IsActive)
        {
            return Error.Rule("Posting.AccountInactive", $"Account '{key.AccountCode}' is not active.");
        }

        if (request.EnforceManualJournalRules && account.IsControlAccount)
        {
            return Error.Rule("Posting.ControlAccountManualJournal",
                $"Account '{key.AccountCode}' is a control account and cannot be posted by manual journal.");
        }

        CurrencyCode lineCurrency = request.TransactionCurrency;
        if (!account.AllowsCurrency(lineCurrency))
        {
            return Error.Rule("Posting.CurrencyNotAllowed",
                $"Account '{key.AccountCode}' is restricted to {account.CurrencyRestriction} and cannot take {lineCurrency}.");
        }

        if (lineRequest.TransactionAmount < 0m)
        {
            return Error.Validation("Posting.NegativeAmount", "Line amounts must be non-negative; use the opposite side instead.");
        }

        // Resolve the exchange rate and base amount.
        decimal rate;
        if (lineCurrency == baseCurrency)
        {
            rate = 1m;
        }
        else if (lineRequest.ExchangeRate is { } explicitRate and > 0m)
        {
            rate = explicitRate;
        }
        else
        {
            Result<decimal> rateResult = await LookupRateAsync(lineCurrency, baseCurrency, request.PostingDate, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            rate = rateResult.Value;
        }

        decimal baseAmount = lineRequest.BaseAmount ?? decimal.Round(lineRequest.TransactionAmount * rate, Money.StorageScale, MidpointRounding.ToEven);

        return lineRequest.Side == JournalSide.Debit
            ? JournalLine.Debit(key, lineCurrency, lineRequest.TransactionAmount, rate, baseAmount, lineRequest.Narrative)
            : JournalLine.Credit(key, lineCurrency, lineRequest.TransactionAmount, rate, baseAmount, lineRequest.Narrative);
    }

    private async Task<Result<(FiscalYear, FiscalPeriod)>> ResolvePeriodAsync(long companyId, string moduleCode, DateOnly date, bool allowFuture, CancellationToken ct)
    {
        List<FiscalYear> years = await _db.FiscalYears
            .Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .Where(y => y.CompanyId == companyId && y.StartDate <= date && date <= y.EndDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (FiscalYear year in years)
        {
            FiscalPeriod? period = year.Periods.FirstOrDefault(p => p.Contains(date));
            if (period is null)
            {
                continue;
            }

            PeriodStatus status = period.StatusFor(moduleCode);
            if (status == PeriodStatus.Open || (status == PeriodStatus.Future && allowFuture))
            {
                return (year, period);
            }

            return Error.Rule("Posting.PeriodNotOpen",
                $"The period containing {date:yyyy-MM-dd} is {status} for module {moduleCode}; posting is not allowed.");
        }

        return Error.Rule("Posting.NoPeriod", $"No fiscal period is defined for {date:yyyy-MM-dd} in company {companyId}.");
    }

    private async Task<Result<decimal>> LookupRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken ct)
    {
        ExchangeRate? rate = await _db.ExchangeRates
            .Where(r => r.FromCurrency == from && r.ToCurrency == to && r.RateType == ExchangeRateType.Daily && r.EffectiveFrom <= date)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return Error.Rule("Posting.NoExchangeRate", $"No daily exchange rate from {from} to {to} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Quotation == QuotationType.Direct ? rate.Rate : 1m / rate.Rate;
    }

    private async Task UpdatePeriodBalancesAsync(long companyId, long fiscalYearId, long fiscalPeriodId, JournalEntry journal, CancellationToken ct)
    {
        foreach (JournalLine line in journal.Lines)
        {
            AccountKey key = line.Key;
            GlPeriodBalance? balance = await _db.PeriodBalances.FirstOrDefaultAsync(
                b => b.CompanyId == companyId && b.FiscalPeriodId == fiscalPeriodId &&
                     b.AccountCode == key.AccountCode && b.CostCentre == key.CostCentre && b.Department == key.Department,
                ct).ConfigureAwait(false);

            if (balance is null)
            {
                balance = new GlPeriodBalance(companyId, fiscalYearId, fiscalPeriodId, key);
                _db.PeriodBalances.Add(balance);
            }

            balance.AddMovement(line.BaseDebit, line.BaseCredit, line.ReportingDebit, line.ReportingCredit);
        }
    }

    private async Task<string> AllocateJournalNumberAsync(long companyId, int year, CancellationToken ct)
    {
        NumberSequence? sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.BranchId == null && s.DocumentType == JournalDocumentType, ct)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            sequence = new NumberSequence(companyId, JournalDocumentType, "JNL-", 6, resetYearly: true);
            _db.NumberSequences.Add(sequence);
        }

        return sequence.Allocate(year);
    }
}
