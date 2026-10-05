using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Schools;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>Keeps student wallets: top-ups into a liability, tuck-shop spends out of it.</summary>
public sealed class StudentWalletService : IStudentWalletService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">Posts the wallet movements.</param>
    public StudentWalletService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> TopUpAsync(long companyId, long studentId, DateOnly date, decimal amount, string bankAccountCode, string walletAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => MoveAsync(companyId, studentId, date, amount, "Top-up", bankAccountCode, walletAccountCode, ct), ct);

    /// <inheritdoc />
    public Task<Result<decimal>> SpendAsync(long companyId, long studentId, DateOnly date, decimal amount, string description, string walletAccountCode, string salesAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => MoveAsync(companyId, studentId, date, -amount, description, walletAccountCode, salesAccountCode, ct), ct);

    // One unit of work: the journal and the wallet entry commit together. A top-up debits the bank and credits the wallet; a spend
    // debits the wallet and credits sales.
    private async Task<Result<decimal>> MoveAsync(long companyId, long studentId, DateOnly date, decimal signed, string description, string debitAccount, string creditAccount, CancellationToken ct)
    {
        if (signed == 0m || string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount) || string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Sch.WalletInvalid", "A wallet movement needs a positive amount, a description and both accounts.");
        }

        Student? student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (student is null)
        {
            return Error.NotFound("Sch.StudentNotFound", $"Student {studentId} not found.");
        }

        decimal balance = await _db.StudentWalletEntries.Where(w => w.StudentId == studentId).SumAsync(w => w.Amount, ct).ConfigureAwait(false);
        if (signed < 0m)
        {
            if (!student.IsEnrolled)
            {
                return Error.Rule("Sch.WalletNotEnrolled", $"{student.FullName} is no longer enrolled; the wallet can only be refunded.");
            }

            if (balance + signed < 0m)
            {
                return Error.Rule("Sch.WalletInsufficient", $"{student.FullName} has {balance:0.00} in their wallet.");
            }

            decimal today = await _db.StudentWalletEntries.Where(w => w.StudentId == studentId && w.Date == date && w.Amount < 0m).SumAsync(w => w.Amount, ct).ConfigureAwait(false);
            if (student.DailySpendLimit is decimal limit && -(today + signed) > limit)
            {
                return Error.Rule("Sch.WalletDailyLimit", $"{student.FullName} may spend {limit:0.00} a day and has spent {-today:0.00} today.");
            }
        }

        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        decimal amount = Math.Abs(signed);
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, EnforceManualJournalRules = false, DocumentType = "StudentWallet",
            DocumentNumber = student.AdmissionNumber, Narrative = $"Wallet {student.AdmissionNumber}: {description.Trim()}",
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        _db.StudentWalletEntries.Add(new StudentWalletEntry(companyId, studentId, date, signed, description.Trim(), posting.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return balance + signed;
    }
}
