using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>
/// Manages period status (brief §3.4): opening, soft-closing and permanently-closing periods per module.
/// The posting engine consults period status before allowing a posting; a permanently-closed period can
/// never reopen (enforced on the aggregate).
/// </summary>
public interface IPeriodService
{
    /// <summary>Sets the status of one period for one module.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="periodNumber">The period number.</param>
    /// <param name="moduleCode">The posting module code.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a rule error for an illegal transition.</returns>
    Task<Result> SetStatusAsync(long companyId, long fiscalYearId, int periodNumber, string moduleCode, PeriodStatus status, CancellationToken ct = default);

    /// <summary>Opens every period of a fiscal year for the given modules.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="moduleCodes">The module codes to open.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<Result> OpenYearAsync(long companyId, long fiscalYearId, IReadOnlyList<string> moduleCodes, CancellationToken ct = default);
}
