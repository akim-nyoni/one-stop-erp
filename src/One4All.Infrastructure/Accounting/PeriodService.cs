using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Manages fiscal-period status per module (brief §3.4).</summary>
public sealed class PeriodService : IPeriodService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PeriodService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetStatusAsync(long companyId, long fiscalYearId, int periodNumber, string moduleCode, PeriodStatus status, CancellationToken ct = default)
    {
        FiscalYear? year = await LoadYearAsync(companyId, fiscalYearId, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Result.Failure(Error.NotFound("Period.YearNotFound", $"Fiscal year {fiscalYearId} not found."));
        }

        FiscalPeriod? period = year.Periods.FirstOrDefault(p => p.PeriodNumber == periodNumber);
        if (period is null)
        {
            return Result.Failure(Error.NotFound("Period.NotFound", $"Period {periodNumber} not found in fiscal year {fiscalYearId}."));
        }

        // A period of a year the year-end has closed is not reopened one module at a time: the year-end is reversed to reopen the year.
        if (year.IsClosed && status is PeriodStatus.Open or PeriodStatus.Future)
        {
            return Result.Failure(Error.Rule("Period.YearClosed", $"Fiscal year {year.Code} has been closed by the year-end; reverse the year-end to reopen its periods."));
        }

        // A permanently closed period can never take another posting, so a draft or deferred journal of that module
        // dated in it would be stranded: it must be posted or deleted first. (A plain close can be reopened.)
        if (status == PeriodStatus.PermanentlyClosed)
        {
            string module = moduleCode.Trim().ToUpperInvariant();
            int unposted = await _db.Journals.CountAsync(j => j.CompanyId == companyId && j.SourceModule == module
                && j.PostingDate >= period.StartDate && j.PostingDate <= period.EndDate
                && (j.Status == JournalStatus.Draft || j.Status == JournalStatus.Deferred), ct).ConfigureAwait(false);
            if (unposted > 0)
            {
                return Result.Failure(Error.Rule("Period.UnpostedJournals",
                    $"{unposted} draft or deferred {module} journal(s) are dated in period {periodNumber}; post or delete them before closing it permanently."));
            }
        }

        Result result = period.SetStatus(moduleCode, status);
        if (result.IsFailure)
        {
            return result;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> OpenYearAsync(long companyId, long fiscalYearId, IReadOnlyList<string> moduleCodes, CancellationToken ct = default)
        => _db.AtomicAsync(() => OpenYearCoreAsync(companyId, fiscalYearId, moduleCodes, ct), ct);

    // One unit of work: every period's status changes together or not at all.
    private async Task<Result> OpenYearCoreAsync(long companyId, long fiscalYearId, IReadOnlyList<string> moduleCodes, CancellationToken ct)
    {
        FiscalYear? year = await LoadYearAsync(companyId, fiscalYearId, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Result.Failure(Error.NotFound("Period.YearNotFound", $"Fiscal year {fiscalYearId} not found."));
        }

        foreach (FiscalPeriod period in year.Periods)
        {
            foreach (string module in moduleCodes)
            {
                Result result = period.SetStatus(module, PeriodStatus.Open);
                if (result.IsFailure)
                {
                    return result;
                }
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<FiscalYear?> LoadYearAsync(long companyId, long fiscalYearId, CancellationToken ct) =>
        await _db.FiscalYears
            .Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, ct)
            .ConfigureAwait(false);
}
