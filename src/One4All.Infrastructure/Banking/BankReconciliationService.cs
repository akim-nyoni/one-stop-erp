using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Imports bank statements and reconciles them to the GL cash book (brief §4.7).</summary>
public sealed class BankReconciliationService : IBankReconciliationService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public BankReconciliationService(One4AllDbContext db) => _db = db;

    private sealed record GlLine(long Id, DateOnly PostingDate, string JournalNumber, decimal Signed);

    private async Task<List<GlLine>> GlLinesAsync(long companyId, string glCode, DateOnly? asAtDate, CancellationToken ct)
    {
        var raw = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && l.AccountCode == glCode &&
                  (asAtDate == null || j.PostingDate <= asAtDate)
            select new { l.Id, j.PostingDate, j.JournalNumber, l.TransactionDebit, l.TransactionCredit })
            .ToListAsync(ct).ConfigureAwait(false);

        return raw.Select(x => new GlLine(x.Id, x.PostingDate, x.JournalNumber, x.TransactionDebit - x.TransactionCredit)).ToList();
    }

    private async Task<Result<BankAccount>> LoadAccountAsync(long companyId, long bankAccountId, CancellationToken ct)
    {
        BankAccount? account = await _db.Set<BankAccount>()
            .FirstOrDefaultAsync(a => a.Id == bankAccountId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        return account is null ? Error.NotFound("Cb.BankNotFound", $"Bank account {bankAccountId} not found.") : account;
    }

    /// <inheritdoc />
    public async Task<Result<int>> ImportStatementLinesAsync(long companyId, long bankAccountId, IReadOnlyList<StatementLineImport> lines, CancellationToken ct = default)
    {
        Result<BankAccount> account = await LoadAccountAsync(companyId, bankAccountId, ct).ConfigureAwait(false);
        if (account.IsFailure)
        {
            return account.Error;
        }

        foreach (StatementLineImport line in lines)
        {
            _db.Set<BankStatementLine>().Add(new BankStatementLine(companyId, bankAccountId, line.StatementDate, line.Description, line.Amount, line.Reference));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return lines.Count;
    }

    /// <inheritdoc />
    public async Task<Result> MatchLineAsync(long companyId, long bankAccountId, long statementLineId, long journalLineId, CancellationToken ct = default)
    {
        Result<BankAccount> accountResult = await LoadAccountAsync(companyId, bankAccountId, ct).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return Result.Failure(accountResult.Error);
        }

        BankStatementLine? line = await _db.Set<BankStatementLine>()
            .FirstOrDefaultAsync(s => s.Id == statementLineId && s.CompanyId == companyId && s.BankAccountId == bankAccountId, ct).ConfigureAwait(false);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Bank.StatementLineNotFound", $"Statement line {statementLineId} not found."));
        }

        if (line.IsReconciled)
        {
            return Result.Failure(Error.Rule("Bank.AlreadyReconciled", "The statement line is already reconciled."));
        }

        List<GlLine> glLines = await GlLinesAsync(companyId, accountResult.Value.GlAccountCode, null, ct).ConfigureAwait(false);
        if (glLines.All(g => g.Id != journalLineId))
        {
            return Result.Failure(Error.Rule("Bank.NotACashBookLine", "The journal line is not a posting on the bank's cash-book account."));
        }

        bool alreadyTaken = await _db.Set<BankStatementLine>()
            .AnyAsync(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && s.MatchedJournalLineId == journalLineId, ct).ConfigureAwait(false);
        if (alreadyTaken)
        {
            return Result.Failure(Error.Rule("Bank.JournalLineMatched", "That cash-book posting is already matched to another statement line."));
        }

        line.MatchTo(journalLineId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> UnmatchLineAsync(long companyId, long statementLineId, CancellationToken ct = default)
    {
        BankStatementLine? line = await _db.Set<BankStatementLine>()
            .FirstOrDefaultAsync(s => s.Id == statementLineId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Bank.StatementLineNotFound", $"Statement line {statementLineId} not found."));
        }

        if (!line.IsReconciled)
        {
            return Result.Failure(Error.Rule("Bank.NotReconciled", "The statement line is not reconciled."));
        }

        line.Unmatch();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> AutoMatchAsync(long companyId, long bankAccountId, CancellationToken ct = default)
    {
        Result<BankAccount> accountResult = await LoadAccountAsync(companyId, bankAccountId, ct).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return accountResult.Error;
        }

        List<BankStatementLine> statementLines = await _db.Set<BankStatementLine>()
            .Where(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && !s.IsReconciled)
            .OrderBy(s => s.StatementDate).ToListAsync(ct).ConfigureAwait(false);

        List<GlLine> glLines = await GlLinesAsync(companyId, accountResult.Value.GlAccountCode, null, ct).ConfigureAwait(false);

        List<long> matchedIds = await _db.Set<BankStatementLine>()
            .Where(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && s.MatchedJournalLineId != null)
            .Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> alreadyMatched = matchedIds.ToHashSet();

        int matched = 0;
        foreach (BankStatementLine line in statementLines)
        {
            GlLine? candidate = glLines.FirstOrDefault(g => !alreadyMatched.Contains(g.Id) && g.Signed == line.Amount);
            if (candidate is not null)
            {
                line.MatchTo(candidate.Id);
                alreadyMatched.Add(candidate.Id);
                matched++;
            }
        }

        if (matched > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return matched;
    }

    /// <inheritdoc />
    public async Task<Result<BankReconciliationReport>> GetReconciliationAsync(long companyId, long bankAccountId, DateOnly asAtDate, CancellationToken ct = default)
    {
        Result<BankAccount> accountResult = await LoadAccountAsync(companyId, bankAccountId, ct).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return accountResult.Error;
        }

        BankAccount account = accountResult.Value;
        List<GlLine> glLines = await GlLinesAsync(companyId, account.GlAccountCode, asAtDate, ct).ConfigureAwait(false);

        List<BankStatementLine> statementLines = await _db.Set<BankStatementLine>()
            .Where(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && s.StatementDate <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal glBalance = glLines.Sum(g => g.Signed);
        decimal statementBalance = statementLines.Sum(s => s.Amount);

        HashSet<long> matchedGlIds = statementLines.Where(s => s.MatchedJournalLineId is not null)
            .Select(s => s.MatchedJournalLineId!.Value).ToHashSet();

        List<ReconciliationItem> unpresented = glLines
            .Where(g => !matchedGlIds.Contains(g.Id))
            .Select(g => new ReconciliationItem(g.PostingDate, g.JournalNumber, g.Signed))
            .ToList();

        List<ReconciliationItem> unmatchedStatement = statementLines
            .Where(s => !s.IsReconciled)
            .Select(s => new ReconciliationItem(s.StatementDate, s.Description, s.Amount))
            .ToList();

        return new BankReconciliationReport(account.Code, asAtDate, glBalance, statementBalance, unpresented, unmatchedStatement);
    }
}
