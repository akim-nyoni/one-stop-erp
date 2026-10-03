using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Retranslates foreign-currency receivables or payables at the closing rate as at a date and books the unrealised
/// difference on the control account against FX gain / loss (see <see cref="ITradingRevaluationService"/>).
/// </summary>
public sealed class TradingRevaluationService : ITradingRevaluationService
{
    private const int Scale = 2;

    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public TradingRevaluationService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<TradingRevaluationResult>> PreviewAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, CancellationToken ct = default)
    {
        Result<Computation> computed = await ComputeAsync(companyId, ledger, asAtDate, ct).ConfigureAwait(false);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        return new TradingRevaluationResult(asAtDate, ledger, computed.Value.Items, computed.Value.Target, computed.Value.Booked, null, null);
    }

    /// <inheritdoc />
    public Task<Result<TradingRevaluationResult>> RevalueAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, bool autoReverse = true, CancellationToken ct = default)
        => _db.AtomicAsync(() => RevalueCoreAsync(companyId, ledger, asAtDate, autoReverse, ct), ct);

    // One unit of work: every posting it makes commits together or not at all (joining a caller's transaction).
    private async Task<Result<TradingRevaluationResult>> RevalueCoreAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, bool autoReverse, CancellationToken ct)
    {
        Result<Computation> computed = await ComputeAsync(companyId, ledger, asAtDate, ct).ConfigureAwait(false);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        (Company company, string controlCode, string gainCode, string lossCode, LedgerSign natural, List<OpenItemRevaluation> items, decimal target, decimal booked) = computed.Value;
        decimal delta = target - booked;
        if (delta == 0m)
        {
            return new TradingRevaluationResult(asAtDate, ledger, items, target, booked, null, null);
        }

        // In the control's natural sign, a positive delta raises the balance: an asset (AR) going up is a gain, a
        // liability (AP) going up is a loss.
        bool controlUp = delta > 0m;
        bool gain = ledger == TradingLedger.AccountsReceivable ? controlUp : !controlUp;
        JournalSide naturalSide = natural == LedgerSign.Debit ? JournalSide.Debit : JournalSide.Credit;
        JournalSide otherSide = naturalSide == JournalSide.Debit ? JournalSide.Credit : JournalSide.Debit;
        JournalSide controlSide = controlUp ? naturalSide : otherSide;
        JournalSide fxSide = controlSide == JournalSide.Debit ? JournalSide.Credit : JournalSide.Debit;
        string fxCode = gain ? gainCode : lossCode;
        decimal magnitude = Math.Abs(delta);
        string module = ledger == TradingLedger.AccountsReceivable ? "AR" : "AP";

        // The revaluation and its reversal stand or fall together.
        Result<PostingReceipt> posted = await _posting.PostAsync(Journal(companyId, company.BaseCurrency, module, asAtDate,
            ITradingRevaluationService.RevaluationDocumentType, $"Unrealised FX revaluation of {module} as at {asAtDate:yyyy-MM-dd}",
            controlCode, controlSide, fxCode, fxSide, magnitude), ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        string? reversalNumber = null;
        if (autoReverse)
        {
            Result<PostingReceipt> reversal = await _posting.PostAsync(Journal(companyId, company.BaseCurrency, module, asAtDate.AddDays(1),
                ITradingRevaluationService.ReversalDocumentType, $"Reversal of {module} FX revaluation of {asAtDate:yyyy-MM-dd}",
                controlCode, fxSide, fxCode, controlSide, magnitude), ct).ConfigureAwait(false);
            if (reversal.IsFailure)
            {
                return reversal.Error;
            }

            reversalNumber = reversal.Value.JournalNumber;
        }
        return new TradingRevaluationResult(asAtDate, ledger, items, target, booked, posted.Value.JournalNumber, reversalNumber);
    }

    private async Task<Result<Computation>> ComputeAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Reval.CompanyNotFound", $"Company {companyId} not found.");
        }

        ControlAccountType controlType = ledger == TradingLedger.AccountsReceivable ? ControlAccountType.AccountsReceivable : ControlAccountType.AccountsPayable;
        string? controlCode = await ControlCodeAsync(companyId, controlType, ct).ConfigureAwait(false);
        string? gainCode = await ControlCodeAsync(companyId, ControlAccountType.FxGain, ct).ConfigureAwait(false);
        string? lossCode = await ControlCodeAsync(companyId, ControlAccountType.FxLoss, ct).ConfigureAwait(false);
        if (controlCode is null || gainCode is null || lossCode is null)
        {
            return Error.Rule("Reval.AccountsMissing", $"The {controlType} control account and the FX gain and loss accounts must all be configured.");
        }

        LedgerSign natural = ledger == TradingLedger.AccountsReceivable ? LedgerSign.Debit : LedgerSign.Credit;
        List<OpenItemAsAt> foreign = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, ledger, asAtDate, ct).ConfigureAwait(false))
            .Where(i => i.Item.Currency != company.BaseCurrency && i.OutstandingTransaction != 0m)
            .OrderBy(i => i.Item.DocumentDate).ThenBy(i => i.Item.Id)];

        Dictionary<CurrencyCode, decimal> rates = [];
        List<OpenItemRevaluation> items = [];
        foreach (OpenItemAsAt entry in foreign)
        {
            CurrencyCode currency = entry.Item.Currency;
            if (!rates.TryGetValue(currency, out decimal rate))
            {
                Result<decimal> closing = await ClosingRateAsync(currency, company.BaseCurrency, asAtDate, ct).ConfigureAwait(false);
                if (closing.IsFailure)
                {
                    return closing.Error;
                }

                rates[currency] = rate = closing.Value;
            }

            decimal outstandingForeign = entry.SignedTransaction(natural);
            decimal bookedBase = entry.Signed(natural);
            decimal revalued = decimal.Round(outstandingForeign * rate, Scale, MidpointRounding.ToEven);
            items.Add(new OpenItemRevaluation(entry.Item.Id, entry.Item.DocumentNumber, entry.Item.TraderId, currency.Value,
                outstandingForeign, bookedBase, rate, revalued, revalued - bookedBase));
        }

        decimal target = items.Sum(i => i.Difference);
        decimal booked = await BookedAsync(_db, companyId, ledger, controlCode, asAtDate, ct).ConfigureAwait(false);
        return new Computation(company, controlCode, gainCode, lossCode, natural, items, target, booked);
    }

    /// <summary>The revaluation worked out but not yet posted.</summary>
    private sealed record Computation(Company Company, string ControlCode, string GainCode, string LossCode, LedgerSign Natural, List<OpenItemRevaluation> Items, decimal Target, decimal Booked);

    /// <summary>
    /// The unrealised revaluation carried on a trading control account at a date, in its natural sign: the net of the
    /// revaluation journals and their reversals posted by then.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">The ledger.</param>
    /// <param name="controlCode">The control account.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The booked revaluation.</returns>
    internal static async Task<decimal> BookedAsync(One4AllDbContext db, long companyId, TradingLedger ledger, string controlCode, DateOnly asAtDate, CancellationToken ct)
    {
        string module = ledger == TradingLedger.AccountsReceivable ? "AR" : "AP";
        var totals = await (
            from l in db.Set<JournalLine>()
            join j in db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAtDate && j.SourceModule == module
                && (j.DocumentType == ITradingRevaluationService.RevaluationDocumentType || j.DocumentType == ITradingRevaluationService.ReversalDocumentType)
                && l.AccountCode == controlCode
            select new { l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);

        decimal net = totals.Sum(t => t.BaseDebit) - totals.Sum(t => t.BaseCredit);
        return ledger == TradingLedger.AccountsReceivable ? net : -net;
    }

    private static JournalRequest Journal(long companyId, CurrencyCode baseCurrency, string module, DateOnly date, string documentType, string narrative,
        string controlCode, JournalSide controlSide, string fxCode, JournalSide fxSide, decimal amount) => new()
    {
        CompanyId = companyId,
        SourceModule = module,
        PostingDate = date,
        TransactionCurrency = baseCurrency,
        EnforceManualJournalRules = false,
        DocumentType = documentType,
        DocumentNumber = $"REVAL-{module}-{date:yyyyMMdd}",
        Narrative = narrative,
        Lines =
        [
            new JournalLineRequest { AccountCode = controlCode, Side = controlSide, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = fxCode, Side = fxSide, TransactionAmount = amount },
        ],
    };

    private async Task<string?> ControlCodeAsync(long companyId, ControlAccountType type, CancellationToken ct)
        => await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == type)
            .OrderBy(a => a.AccountCode)
            .Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private async Task<Result<decimal>> ClosingRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken ct)
    {
        decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, from, to, ExchangeRateType.ClosingRate, date, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Reval.NoClosingRate", $"No closing rate from {from} to {to} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Value;
    }
}
