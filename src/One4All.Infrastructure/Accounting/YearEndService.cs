using Microsoft.EntityFrameworkCore;
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
    public Task<Result<YearEndResult>> RunAsync(long companyId, long fiscalYearId, string retainedEarningsAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, fiscalYearId, retainedEarningsAccountCode, ct), ct);

    // One unit of work: every posting it makes commits together or not at all (joining a caller's transaction).
    private async Task<Result<YearEndResult>> RunCoreAsync(long companyId, long fiscalYearId, string retainedEarningsAccountCode, CancellationToken ct)
    {
        FiscalYear? year = await _db.FiscalYears
            .Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, ct)
            .ConfigureAwait(false);

        if (year is null)
        {
            return Error.NotFound("YearEnd.YearNotFound", $"Fiscal year {fiscalYearId} not found.");
        }

        if (year.IsHardLocked)
        {
            return Error.Rule("YearEnd.HardLocked", $"Fiscal year {year.Code} is hard-locked.");
        }

        if (year.IsClosed)
        {
            return Error.Rule("YearEnd.AlreadyClosed", $"Fiscal year {year.Code} is already closed; reverse its year-end to run it again.");
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
                ToAdjustmentPeriod = year.AdjustmentPeriod is not null,
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

        // Close every period of the year just ended (all modules that had a status); hard-locking later makes it permanent.
        foreach (FiscalPeriod period in year.Periods)
        {
            foreach (PeriodModuleStatus moduleStatus in period.ModuleStatuses.ToList())
            {
                period.SetStatus(moduleStatus.ModuleCode, PeriodStatus.Closed);
            }
        }

        year.Close();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new YearEndResult(closingJournalNumber, netProfit, nextYearId);
    }

    // The next year starts the day after this one ends and follows the same calendar (monthly or 4-4-5, with or without an
    // adjustment period); an existing year starting that day is used as it is.
    private async Task<long> EnsureNextYearAsync(long company, FiscalYear year, CancellationToken ct)
    {
        DateOnly nextStart = year.EndDate.AddDays(1);
        FiscalYear? existing = await _db.FiscalYears.FirstOrDefaultAsync(y => y.CompanyId == company && y.StartDate <= nextStart && y.EndDate >= nextStart, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        List<(DateOnly Start, DateOnly End)> ordinary = [.. year.OrderedPeriods.Where(p => !p.IsAdjustment).Select(p => (p.StartDate, p.EndDate))];
        FiscalCalendar calendar = FiscalCalendars.Detect(ordinary);
        IReadOnlyList<(DateOnly Start, DateOnly End)> periods = FiscalCalendars.Periods(calendar, nextStart, Math.Max(ordinary.Count, 1));
        string code = FiscalCalendars.NextCode(year.Code, nextStart);
        if (await _db.FiscalYears.AnyAsync(y => y.CompanyId == company && y.Code == code, ct).ConfigureAwait(false))
        {
            code = $"{code}-{nextStart:yyyyMMdd}";
        }

        FiscalYear next = new(company, code, nextStart, periods[^1].End);
        for (int n = 0; n < periods.Count; n++)
        {
            next.AddPeriod(n + 1, calendar == FiscalCalendar.FourFourFive ? $"P{n + 1} {code}" : periods[n].Start.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                periods[n].Start, periods[n].End);
        }

        if (year.AdjustmentPeriod is not null)
        {
            next.AddAdjustmentPeriod();
        }

        _db.FiscalYears.Add(next);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return next.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> ReverseAsync(long companyId, long fiscalYearId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReverseCoreAsync(companyId, fiscalYearId, reason, ct), ct);

    private async Task<Result<string>> ReverseCoreAsync(long companyId, long fiscalYearId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("YearEnd.ReasonRequired", "Reversing a year-end needs a reason.");
        }

        FiscalYear? year = await _db.FiscalYears.Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("YearEnd.YearNotFound", $"Fiscal year {fiscalYearId} not found.");
        }

        if (!year.IsClosed)
        {
            return Error.Rule("YearEnd.NotClosed", $"Fiscal year {year.Code} has not been closed.");
        }

        Result reopened = year.Reopen();
        if (reopened.IsFailure)
        {
            return reopened.Error;
        }

        // The closing journal is reversed in its own period, opened for the general ledger just for that.
        JournalEntry? closing = await _db.Journals
            .Where(j => j.CompanyId == companyId && j.DocumentType == "YearEndClose" && j.PostingDate == year.EndDate && j.Status == JournalStatus.Posted && !j.IsReversal)
            .OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        bool reversedAlready = closing is not null
            && await _db.Journals.AnyAsync(j => j.CompanyId == companyId && j.ReversalOfJournalId == closing.Id, ct).ConfigureAwait(false);
        string reversal = "(none)";
        if (closing is not null && !reversedAlready)
        {
            FiscalPeriod period = year.Periods.Single(p => p.Id == closing.FiscalPeriodId);
            Result open = period.SetStatus("GL", PeriodStatus.Open);
            if (open.IsFailure)
            {
                return open.Error;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _postingEngine.ReverseAsync(companyId, closing.Id, year.EndDate, $"Year-end {year.Code} reversed: {reason.Trim()}", ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            reversal = posted.Value.JournalNumber;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reversal;
    }

    /// <inheritdoc />
    public async Task<Result> HardLockAsync(long companyId, long fiscalYearId, CancellationToken ct = default)
    {
        FiscalYear? year = await _db.FiscalYears.Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Result.Failure(Error.NotFound("YearEnd.YearNotFound", $"Fiscal year {fiscalYearId} not found."));
        }

        if (!year.IsClosed)
        {
            return Result.Failure(Error.Rule("YearEnd.NotClosed", $"Run the year-end for {year.Code} before hard-locking it."));
        }

        foreach (FiscalPeriod period in year.Periods)
        {
            foreach (string module in period.ModuleStatuses.Select(m => m.ModuleCode).Append("GL").Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                period.SetStatus(module, PeriodStatus.PermanentlyClosed);
            }
        }

        year.HardLock();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
