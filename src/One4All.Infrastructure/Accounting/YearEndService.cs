using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Runs the year-end routine (brief §3.4): posts the profit-or-loss closing journal to retained earnings,
/// opens the next fiscal year, and permanently closes the year just ended. Balance-sheet balances carry
/// forward naturally because the reports accumulate from inception.
/// </summary>
public sealed class YearEndService : IYearEndService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IGeneralLedgerReports _reports;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine (the closing journal goes through it).</param>
    /// <param name="reports">The GL reports (used to compute the year's P&amp;L).</param>
    public YearEndService(One4AllDbContext db, IPostingEngine postingEngine, IGeneralLedgerReports reports)
    {
        _db = db;
        _postingEngine = postingEngine;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<YearEndResult>> RunAsync(long companyId, long fiscalYearId, string retainedEarningsAccountCode, CancellationToken ct = default)
    {
        FiscalYear? year = await _db.FiscalYears
            .Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, ct)
            .ConfigureAwait(false);

        if (year is null)
        {
            return Error.NotFound("YearEnd.YearNotFound", $"Fiscal year {fiscalYearId} not found.");
        }

        if (year.IsClosed)
        {
            return Error.Rule("YearEnd.AlreadyClosed", $"Fiscal year {year.Code} is already closed.");
        }

        string retained = retainedEarningsAccountCode.Trim().ToUpperInvariant();
        bool retainedExists = await _db.Accounts
            .AnyAsync(a => a.CompanyId == companyId && a.AccountCode == retained && a.Type == AccountType.Equity, ct)
            .ConfigureAwait(false);
        if (!retainedExists)
        {
            return Error.Rule("YearEnd.NoRetainedEarnings", $"Retained earnings account '{retained}' (equity) was not found.");
        }

        // Draft or deferred journals dated in the year would be left out of the closing profit and could never be posted
        // once the year's periods are permanently closed: they must be posted (or deleted) first.
        List<string> unposted = await _db.Journals
            .Where(j => j.CompanyId == companyId && j.PostingDate >= year.StartDate && j.PostingDate <= year.EndDate
                && (j.Status == JournalStatus.Draft || j.Status == JournalStatus.Deferred))
            .OrderBy(j => j.PostingDate)
            .Select(j => j.JournalNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        if (unposted.Count > 0)
        {
            string sample = string.Join(", ", unposted.Where(n => n.Length > 0).Take(5));
            return Error.Rule("YearEnd.UnpostedJournals",
                $"{unposted.Count} draft or deferred journal(s) are dated in {year.Code}{(sample.Length > 0 ? $" ({sample})" : string.Empty)}; post or delete them before closing the year.");
        }

        // The closing journal, the next year and the closed periods stand or fall together.
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Compute the year's profit or loss and build the closing journal that zeroes income/expense.
        IncomeStatement pl = await _reports.IncomeStatementAsync(companyId, year.StartDate, year.EndDate, ct).ConfigureAwait(false);

        List<JournalLineRequest> lines = [];
        foreach (StatementLine income in pl.Income)
        {
            lines.Add(new JournalLineRequest { AccountCode = income.AccountCode, Side = JournalSide.Debit, TransactionAmount = income.Amount });
        }

        foreach (StatementLine expense in pl.Expenses)
        {
            lines.Add(new JournalLineRequest { AccountCode = expense.AccountCode, Side = JournalSide.Credit, TransactionAmount = expense.Amount });
        }

        decimal netProfit = pl.NetProfit;
        if (netProfit != 0m)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = retained,
                Side = netProfit > 0m ? JournalSide.Credit : JournalSide.Debit,
                TransactionAmount = Math.Abs(netProfit),
            });
        }

        string closingJournalNumber = "(none)";
        if (lines.Count > 0)
        {
            Company company = (await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false));
            JournalRequest request = new()
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = year.EndDate,
                TransactionCurrency = company.BaseCurrency,
                EnforceManualJournalRules = false,
                DocumentType = "YearEndClose",
                Narrative = $"Year-end close {year.Code}",
                Lines = lines,
            };

            Result<PostingReceipt> posting = await _postingEngine.PostAsync(request, ct).ConfigureAwait(false);
            if (posting.IsFailure)
            {
                return posting.Error;
            }

            closingJournalNumber = posting.Value.JournalNumber;
        }

        // Create the next fiscal year (if it does not yet exist) with 12 future periods.
        long nextYearId = await EnsureNextYearAsync(company: companyId, year, ct).ConfigureAwait(false);

        // Permanently close every period of the year just ended (all modules that had a status).
        foreach (FiscalPeriod period in year.Periods)
        {
            foreach (PeriodModuleStatus moduleStatus in period.ModuleStatuses.ToList())
            {
                period.SetStatus(moduleStatus.ModuleCode, PeriodStatus.PermanentlyClosed);
            }
        }

        year.Close();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new YearEndResult(closingJournalNumber, netProfit, nextYearId);
    }

    private async Task<long> EnsureNextYearAsync(long company, FiscalYear year, CancellationToken ct)
    {
        int nextYearNumber = year.EndDate.Year + 1;
        string nextCode = nextYearNumber.ToString();

        FiscalYear? existing = await _db.FiscalYears
            .FirstOrDefaultAsync(y => y.CompanyId == company && y.Code == nextCode, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        DateOnly start = new(nextYearNumber, 1, 1);
        FiscalYear next = new(company, nextCode, start, new DateOnly(nextYearNumber, 12, 31));
        for (int month = 1; month <= 12; month++)
        {
            DateOnly periodStart = new(nextYearNumber, month, 1);
            next.AddPeriod(month, periodStart.ToString("MMMM"), periodStart, periodStart.AddMonths(1).AddDays(-1));
        }

        _db.FiscalYears.Add(next);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return next.Id;
    }
}
