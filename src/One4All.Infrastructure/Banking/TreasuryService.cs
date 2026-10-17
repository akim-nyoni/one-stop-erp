using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Bank-to-bank transfers through the posting engine.</summary>
public sealed class TreasuryService : ITreasuryService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public TreasuryService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<BankTransferResult>> TransferAsync(BankTransferRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => TransferCoreAsync(request, ct), ct);

    // One unit: every leg of the transfer posts, or none does.
    private async Task<Result<BankTransferResult>> TransferCoreAsync(BankTransferRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FromBankAccountId == request.ToBankAccountId || request.AmountSent <= 0m || request.AmountReceived <= 0m)
        {
            return Error.Validation("Treasury.Invalid", "A transfer moves a positive amount between two different accounts.");
        }

        List<BankAccount> accounts = await _db.Set<BankAccount>()
            .Where(b => b.CompanyId == request.CompanyId && (b.Id == request.FromBankAccountId || b.Id == request.ToBankAccountId) && b.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);
        BankAccount? from = accounts.FirstOrDefault(b => b.Id == request.FromBankAccountId);
        BankAccount? to = accounts.FirstOrDefault(b => b.Id == request.ToBankAccountId);
        if (from is null || to is null)
        {
            return Error.NotFound("Treasury.AccountNotFound", "Both bank accounts must exist and be active.");
        }

        CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        string narrative = $"Transfer {from.Code} to {to.Code}";

        if (from.Currency == to.Currency)
        {
            if (request.AmountReceived is decimal received && received != request.AmountSent)
            {
                return Error.Validation("Treasury.Amounts", "A transfer in one currency arrives as sent.");
            }

            Result<PostingReceipt> single = await PostAsync(request, from.Currency, narrative,
                [Line(to.GlAccountCode, JournalSide.Debit, request.AmountSent, null, null), Line(from.GlAccountCode, JournalSide.Credit, request.AmountSent, null, null)], ct).ConfigureAwait(false);
            if (single.IsFailure)
            {
                return single.Error;
            }

            return new BankTransferResult([single.Value.JournalNumber], single.Value.TotalBase, single.Value.TotalBase);
        }

        if (request.AmountReceived is not decimal amountReceived || string.IsNullOrWhiteSpace(request.ClearingAccountCode)
            || string.IsNullOrWhiteSpace(request.FxGainAccountCode) || string.IsNullOrWhiteSpace(request.FxLossAccountCode))
        {
            return Error.Validation("Treasury.CrossCurrency", "A transfer between currencies needs the amount received, a clearing account and exchange gain and loss accounts.");
        }

        decimal? rateFrom = await ExchangeRateResolver.PolicyRateAsync(_db, request.CompanyId, from.Currency, baseCurrency, "Treasury", "CB", request.Date, ct).ConfigureAwait(false);
        decimal? rateTo = await ExchangeRateResolver.PolicyRateAsync(_db, request.CompanyId, to.Currency, baseCurrency, "Treasury", "CB", request.Date, ct).ConfigureAwait(false);
        if (rateFrom is null || rateTo is null)
        {
            return Error.Rule("Treasury.NoRate", $"There is no daily rate for {from.Currency} or {to.Currency} on {request.Date:yyyy-MM-dd}.");
        }

        decimal baseSent = decimal.Round(request.AmountSent * rateFrom.Value, 2, MidpointRounding.AwayFromZero);
        decimal baseReceived = decimal.Round(amountReceived * rateTo.Value, 2, MidpointRounding.AwayFromZero);
        string clearing = request.ClearingAccountCode.Trim();
        List<string> journals = [];

        // Each leg in its own currency, through the clearing account.
        Result<PostingReceipt> sent = await PostAsync(request, from.Currency, narrative + " (sent)",
            [Line(clearing, JournalSide.Debit, request.AmountSent, rateFrom, baseSent), Line(from.GlAccountCode, JournalSide.Credit, request.AmountSent, rateFrom, baseSent)], ct).ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return sent.Error;
        }

        journals.Add(sent.Value.JournalNumber);
        Result<PostingReceipt> arrived = await PostAsync(request, to.Currency, narrative + " (received)",
            [Line(to.GlAccountCode, JournalSide.Debit, amountReceived, rateTo, baseReceived), Line(clearing, JournalSide.Credit, amountReceived, rateTo, baseReceived)], ct).ConfigureAwait(false);
        if (arrived.IsFailure)
        {
            return arrived.Error;
        }

        journals.Add(arrived.Value.JournalNumber);

        // What is left on the clearing account is the exchange difference.
        decimal difference = baseReceived - baseSent;
        if (difference != 0m)
        {
            JournalLineRequest[] fx = difference > 0m
                ? [Line(clearing, JournalSide.Debit, difference, null, null), Line(request.FxGainAccountCode.Trim(), JournalSide.Credit, difference, null, null)]
                : [Line(request.FxLossAccountCode.Trim(), JournalSide.Debit, -difference, null, null), Line(clearing, JournalSide.Credit, -difference, null, null)];
            Result<PostingReceipt> realised = await PostAsync(request, baseCurrency, narrative + " (exchange difference)", fx, ct).ConfigureAwait(false);
            if (realised.IsFailure)
            {
                return realised.Error;
            }

            journals.Add(realised.Value.JournalNumber);
        }

        return new BankTransferResult(journals, baseSent, baseReceived);
    }

    private static JournalLineRequest Line(string account, JournalSide side, decimal amount, decimal? rate, decimal? baseAmount)
        => new() { AccountCode = account, Side = side, TransactionAmount = amount, ExchangeRate = rate, BaseAmount = baseAmount };

    private Task<Result<PostingReceipt>> PostAsync(BankTransferRequest request, CurrencyCode currency, string narrative, IReadOnlyList<JournalLineRequest> lines, CancellationToken ct)
        => _engine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "CB",
            PostingDate = request.Date,
            TransactionCurrency = currency,
            DocumentType = "BankTransfer",
            DocumentNumber = request.Reference,
            Narrative = narrative,
            Lines = lines,
        }, ct);
}
