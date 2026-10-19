using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>
/// Posts cash-book entries from bank statement lines through the ordinary ledgers and matches them, inside one
/// database transaction so a posting never exists without its match (or vice versa).
/// </summary>
public sealed class BankStatementPostingService : IBankStatementPostingService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _sales;
    private readonly IPurchaseLedgerService _purchases;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="sales">The sales ledger (customer receipts).</param>
    /// <param name="purchases">The purchase ledger (supplier payments).</param>
    /// <param name="posting">The posting engine (direct-to-account entries).</param>
    public BankStatementPostingService(One4AllDbContext db, ISalesLedgerService sales, IPurchaseLedgerService purchases, IPostingEngine posting)
    {
        _db = db;
        _sales = sales;
        _purchases = purchases;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<StatementLinePosting>> PostCustomerReceiptAsync(long companyId, long bankAccountId, long statementLineId, long customerId, string? documentNumber = null, CancellationToken ct = default) =>
        PostAndMatchAsync(companyId, bankAccountId, statementLineId, mustBePositive: true, async (bank, line) =>
        {
            Result<TradingPostingResult> receipt = await _sales.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId,
                CustomerId = customerId,
                DocumentNumber = Number(documentNumber, line),
                ReceiptDate = line.StatementDate,
                Currency = bank.Currency,
                Amount = line.Amount,
                BankAccountCode = bank.GlAccountCode,
                Narrative = line.Description,
            }, ct).ConfigureAwait(false);
            if (receipt.IsFailure)
            {
                return receipt.Error;
            }

            return (receipt.Value.JournalNumber, (long?)receipt.Value.OpenItemId);
        }, ct);

    /// <inheritdoc />
    public Task<Result<StatementLinePosting>> PostSupplierPaymentAsync(long companyId, long bankAccountId, long statementLineId, long supplierId, string? documentNumber = null, CancellationToken ct = default) =>
        PostAndMatchAsync(companyId, bankAccountId, statementLineId, mustBePositive: false, async (bank, line) =>
        {
            Result<TradingPostingResult> payment = await _purchases.PostPaymentAsync(new PaymentRequest
            {
                CompanyId = companyId,
                SupplierId = supplierId,
                DocumentNumber = Number(documentNumber, line),
                PaymentDate = line.StatementDate,
                Currency = bank.Currency,
                Amount = -line.Amount,
                BankAccountCode = bank.GlAccountCode,
                Narrative = line.Description,
            }, ct).ConfigureAwait(false);
            if (payment.IsFailure)
            {
                return payment.Error;
            }

            return (payment.Value.JournalNumber, (long?)payment.Value.OpenItemId);
        }, ct);

    /// <inheritdoc />
    public Task<Result<StatementLinePosting>> PostToAccountAsync(long companyId, long bankAccountId, long statementLineId, string accountCode, string? narrative = null, CancellationToken ct = default,
        string? taxCode = null) =>
        PostAndMatchAsync(companyId, bankAccountId, statementLineId, mustBePositive: null, async (bank, line) =>
        {
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                return Error.Validation("Bank.AccountRequired", "Choose the account to post the statement line to.");
            }

            // Sub-ledger controls are kept in step with their ledgers, so money for customers, suppliers and stock
            // never goes straight to them. VAT and bank controls are allowed (paying VAT, moving money between banks).
            string contra = accountCode.Trim().ToUpperInvariant();
            ControlAccountType control = await _db.Accounts
                .Where(a => a.CompanyId == companyId && a.AccountCode == contra)
                .Select(a => a.ControlAccountType)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (control is ControlAccountType.AccountsReceivable or ControlAccountType.AccountsPayable or ControlAccountType.Stock or ControlAccountType.GoodsReceivedNotInvoiced)
            {
                return Error.Rule("Bank.ControlAccount", $"{contra} is the {control} control account; post customer, supplier and stock money through their ledgers instead.");
            }

            decimal amount = Math.Abs(line.Amount);
            bool moneyIn = line.Amount > 0m;
            List<JournalLineRequest> lines =
            [
                new JournalLineRequest { AccountCode = bank.GlAccountCode, Side = moneyIn ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = amount },
            ];
            if (!string.IsNullOrWhiteSpace(taxCode))
            {
                // VAT inside the amount: the net to the account, the VAT to the code's input (money out) or output (money in) account, both tagged for the return.
                Result<Accounting.GrossTaxSplit> split = await Accounting.GrossTaxSplit.SplitAsync(_db, companyId, taxCode, amount, line.StatementDate, purchase: !moneyIn, ct).ConfigureAwait(false);
                if (split.IsFailure)
                {
                    return split.Error;
                }

                lines.Add(new JournalLineRequest { AccountCode = contra, Side = moneyIn ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = split.Value.Net, TaxCode = split.Value.Code });
                if (split.Value.Tax != 0m)
                {
                    lines.Add(new JournalLineRequest { AccountCode = split.Value.TaxAccountCode, Side = moneyIn ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = split.Value.Tax, TaxCode = split.Value.Code });
                }
            }
            else
            {
                lines.Add(new JournalLineRequest { AccountCode = contra, Side = moneyIn ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = amount });
            }

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "CB",
                PostingDate = line.StatementDate,
                TransactionCurrency = bank.Currency,
                EnforceManualJournalRules = false,
                DocumentType = "BANK",
                DocumentNumber = Number(null, line),
                Reference = line.Reference,
                Narrative = string.IsNullOrWhiteSpace(narrative) ? line.Description : narrative.Trim(),
                Lines = lines,
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            return (posted.Value.JournalNumber, (long?)null);
        }, ct);

    private async Task<Result<StatementLinePosting>> PostAndMatchAsync(long companyId, long bankAccountId, long statementLineId, bool? mustBePositive,
        Func<BankAccount, BankStatementLine, Task<Result<(string JournalNumber, long? OpenItemId)>>> post, CancellationToken ct)
    {
        BankAccount? bank = await _db.Set<BankAccount>().FirstOrDefaultAsync(b => b.Id == bankAccountId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (bank is null)
        {
            return Error.NotFound("Cb.BankNotFound", $"Bank account {bankAccountId} not found.");
        }

        BankStatementLine? line = await _db.Set<BankStatementLine>()
            .FirstOrDefaultAsync(s => s.Id == statementLineId && s.CompanyId == companyId && s.BankAccountId == bankAccountId, ct).ConfigureAwait(false);
        if (line is null)
        {
            return Error.NotFound("Bank.StatementLineNotFound", $"Statement line {statementLineId} not found.");
        }

        if (line.IsReconciled)
        {
            return Error.Rule("Bank.AlreadyReconciled", "The statement line is already reconciled.");
        }

        if (mustBePositive == true && line.Amount <= 0m)
        {
            return Error.Rule("Bank.NotMoneyIn", "Only a money-in statement line can be posted as a customer receipt.");
        }

        if (mustBePositive == false && line.Amount >= 0m)
        {
            return Error.Rule("Bank.NotMoneyOut", "Only a money-out statement line can be posted as a supplier payment.");
        }

        // The posting and the match stand or fall together (joining a caller's transaction when there is one).
        return await _db.AtomicAsync<StatementLinePosting>(async () =>
        {
            Result<(string JournalNumber, long? OpenItemId)> posted = await post(bank, line).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            // The bank leg of the journal just posted is what the statement line matches.
            long? bankLegId = await (
                from l in _db.Set<JournalLine>()
                join j in _db.Journals on l.JournalEntryId equals j.Id
                where j.CompanyId == companyId && j.JournalNumber == posted.Value.JournalNumber && l.AccountCode == bank.GlAccountCode
                select (long?)l.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (bankLegId is null)
            {
                return Error.Failure("Bank.NoBankLeg", $"Journal {posted.Value.JournalNumber} has no line on the bank account {bank.GlAccountCode}.");
            }

            line.MatchTo(bankLegId.Value);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new StatementLinePosting(posted.Value.JournalNumber, bankLegId.Value, posted.Value.OpenItemId);
        }, ct).ConfigureAwait(false);
    }

    // The document number: as given, else the bank's reference, else a stable BNK-{line id}.
    private static string Number(string? documentNumber, BankStatementLine line) =>
        !string.IsNullOrWhiteSpace(documentNumber) ? documentNumber.Trim()
        : !string.IsNullOrWhiteSpace(line.Reference) ? line.Reference.Trim()
        : $"BNK-{line.Id}";
}
