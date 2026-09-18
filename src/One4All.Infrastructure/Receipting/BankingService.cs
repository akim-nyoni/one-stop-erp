using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// Banking &amp; treasury (brief §RCP-10). Moves counted takings into a cash-in-transit control account and
/// clears it to the bank when the statement confirms — cash-in-transit is its own control that must clear.
/// </summary>
public sealed class BankingService : IBankingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public BankingService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<BankingResult>> BankCashAsync(long companyId, decimal amount, DateOnly date, string reference, string cashAccountCode, string cashInTransitAccountCode, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The banking amount must be positive.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        BankDeposit deposit = new(companyId, reference, company.BaseCurrency, amount, date);
        _db.Set<BankDeposit>().Add(deposit);

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "BANKING",
            DocumentNumber = reference,
            Narrative = $"Banking to cash-in-transit ({reference})",
            Lines =
            [
                new JournalLineRequest { AccountCode = cashInTransitAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BankingResult(deposit.Id, reference, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result<string>> ConfirmBankedAsync(long companyId, long depositId, DateOnly date, string cashInTransitAccountCode, string bankAccountCode, CancellationToken ct = default)
    {
        BankDeposit? deposit = await _db.Set<BankDeposit>().FirstOrDefaultAsync(d => d.Id == depositId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        if (deposit is null)
        {
            return Error.NotFound("Rcp.DepositNotFound", $"Deposit {depositId} not found.");
        }

        if (deposit.Status == BankDepositStatus.Banked)
        {
            return Error.Rule("Rcp.AlreadyBanked", $"Deposit {depositId} is already confirmed banked.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "BANKED",
            DocumentNumber = deposit.Reference,
            Narrative = $"Bank statement confirmation ({deposit.Reference})",
            Lines =
            [
                new JournalLineRequest { AccountCode = bankAccountCode, Side = JournalSide.Debit, TransactionAmount = deposit.Amount },
                new JournalLineRequest { AccountCode = cashInTransitAccountCode, Side = JournalSide.Credit, TransactionAmount = deposit.Amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        deposit.MarkBanked(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }
}
