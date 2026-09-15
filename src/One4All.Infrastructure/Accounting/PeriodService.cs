using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
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

        Result result = period.SetStatus(moduleCode, status);
        if (result.IsFailure)
        {
            return result;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> OpenYearAsync(long companyId, long fiscalYearId, IReadOnlyList<string> moduleCodes, CancellationToken ct = default)
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
