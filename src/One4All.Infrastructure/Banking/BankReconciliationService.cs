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

    private sealed record GlLine(long Id, DateOnly PostingDate, string JournalNumber, decimal Signed, string? DocumentNumber, string? Reference);

    private async Task<List<GlLine>> GlLinesAsync(long companyId, string glCode, DateOnly? asAtDate, CancellationToken ct)
    {
        var raw = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && l.AccountCode == glCode &&
                  (asAtDate == null || j.PostingDate <= asAtDate)
            select new { l.Id, j.PostingDate, j.JournalNumber, l.TransactionDebit, l.TransactionCredit, j.DocumentNumber, j.Reference })
            .ToListAsync(ct).ConfigureAwait(false);

        return raw.Select(x => new GlLine(x.Id, x.PostingDate, x.JournalNumber, x.TransactionDebit - x.TransactionCredit, x.DocumentNumber, x.Reference)).ToList();
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
    public Task<Result<int>> ImportStatementCsvAsync(long companyId, long bankAccountId, string csv, One4All.Application.Banking.BankCsvLayout layout, CancellationToken ct = default)
        => ImportStatementFileAsync(companyId, bankAccountId, csv, One4All.Application.Banking.StatementFileFormat.Csv, layout, ct);

    /// <inheritdoc />
    public async Task<Result<int>> ImportStatementFileAsync(long companyId, long bankAccountId, string content, One4All.Application.Banking.StatementFileFormat format, One4All.Application.Banking.BankCsvLayout? csvLayout = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Error.Validation("Cb.EmptyStatement", "The statement file is empty.");
        }

        One4All.Application.Banking.BankCsvParseResult parsed = format switch
        {
            One4All.Application.Banking.StatementFileFormat.Mt940 => One4All.Application.Banking.Mt940Parser.Parse(content),
            One4All.Application.Banking.StatementFileFormat.Camt053 => One4All.Application.Banking.Camt053Parser.Parse(content),
            _ => One4All.Application.Banking.BankStatementCsvParser.Parse(content, csvLayout ?? One4All.Application.Banking.BankCsvLayout.Simple),
        };
        if (!parsed.IsClean)
        {
            string detail = string.Join("; ", parsed.Errors.Take(5).Select(e => $"line {e.LineNumber}: {e.Reason}"));
            string more = parsed.Errors.Count > 5 ? $" (+{parsed.Errors.Count - 5} more)" : string.Empty;
            return Error.Validation("Cb.StatementParse", $"{parsed.Errors.Count} line(s) could not be read — {detail}{more}.");
        }

        if (parsed.Lines.Count == 0)
        {
            return Error.Validation("Cb.NoStatementLines", "The statement file has no data lines.");
        }

        return await ImportStatementLinesAsync(companyId, bankAccountId, parsed.Lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<CashPositionReport>> CashPositionAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<BankAccount> accounts = await _db.Set<BankAccount>()
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        List<CashPositionRow> rows = [];
        foreach (BankAccount account in accounts)
        {
            List<GlLine> glLines = await GlLinesAsync(companyId, account.GlAccountCode, asAtDate, ct).ConfigureAwait(false);
            decimal balance = glLines.Sum(g => g.Signed);
            int unreconciled = await _db.Set<BankStatementLine>()
                .CountAsync(s => s.CompanyId == companyId && s.BankAccountId == account.Id && !s.IsReconciled && s.StatementDate <= asAtDate, ct)
                .ConfigureAwait(false);

            rows.Add(new CashPositionRow(account.Code, account.Name, account.Currency.Value, account.GlAccountCode, balance, unreconciled));
        }

        return new CashPositionReport(asAtDate, rows, rows.Sum(r => r.Balance));
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
    public async Task<Result<int>> AutoMatchAsync(long companyId, long bankAccountId, int dateToleranceDays = One4All.Application.Banking.BankAutoMatcher.DefaultDateToleranceDays, CancellationToken ct = default)
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

        HashSet<long> alreadyMatched = (await _db.Set<BankStatementLine>()
            .Where(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && s.MatchedJournalLineId != null)
            .Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        // The matching rule itself is pure: same amount, within the date window, reference then closest date.
        IReadOnlyList<One4All.Application.Banking.BankMatch> matches = One4All.Application.Banking.BankAutoMatcher.Match(
            statementLines.Select(s => new One4All.Application.Banking.MatchableStatementLine(s.Id, s.StatementDate, s.Amount, s.Reference)).ToList(),
            glLines.Where(g => !alreadyMatched.Contains(g.Id)).Select(g => new One4All.Application.Banking.MatchableGlLine(g.Id, g.PostingDate, g.Signed, g.DocumentNumber, g.Reference)).ToList(),
            dateToleranceDays);

        Dictionary<long, BankStatementLine> byId = statementLines.ToDictionary(s => s.Id);
        foreach (One4All.Application.Banking.BankMatch match in matches)
        {
            byId[match.StatementLineId].MatchTo(match.GlLineId);
        }

        if (matches.Count > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return matches.Count;
    }

    /// <inheritdoc />
    public async Task<Result<BankWorkList>> WorkListAsync(long companyId, long bankAccountId, DateOnly asAtDate, CancellationToken ct = default)
    {
        Result<BankAccount> accountResult = await LoadAccountAsync(companyId, bankAccountId, ct).ConfigureAwait(false);
        if (accountResult.IsFailure)
        {
            return accountResult.Error;
        }

        List<GlLine> glLines = await GlLinesAsync(companyId, accountResult.Value.GlAccountCode, asAtDate, ct).ConfigureAwait(false);
        List<BankStatementLine> statementLines = await _db.Set<BankStatementLine>()
            .Where(s => s.CompanyId == companyId && s.BankAccountId == bankAccountId && s.StatementDate <= asAtDate)
            .OrderBy(s => s.StatementDate).ThenBy(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        HashSet<long> matchedGlIds = statementLines.Where(s => s.MatchedJournalLineId is not null).Select(s => s.MatchedJournalLineId!.Value).ToHashSet();

        return new BankWorkList(
            statementLines.Select(s => new StatementWorkItem(s.Id, s.StatementDate, s.Description, s.Reference, s.Amount, s.MatchedJournalLineId)).ToList(),
            glLines.Where(g => !matchedGlIds.Contains(g.Id)).OrderBy(g => g.PostingDate).ThenBy(g => g.Id)
                .Select(g => new GlWorkItem(g.Id, g.PostingDate, g.JournalNumber, g.DocumentNumber, g.Signed)).ToList());
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
