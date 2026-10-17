using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The result of a year-end close.</summary>
/// <param name="ClosingJournalNumber">The number of the P&amp;L closing journal.</param>
/// <param name="NetProfitClosed">The net profit rolled to retained earnings.</param>
/// <param name="NextYearId">The id of the newly created (or existing) next fiscal year.</param>
public readonly record struct YearEndResult(string ClosingJournalNumber, decimal NetProfitClosed, long NextYearId);

/// <summary>
/// Runs the year-end routine (brief §3.4): rolls the profit or loss to retained earnings via a closing journal (in the adjustment
/// period when the year has one), opens the next year on the same calendar, and closes the year - reversible and re-runnable until
/// it is hard-locked.
/// Balance-sheet balances carry forward naturally because reports accumulate from inception.
/// </summary>
public interface IYearEndService
{
    /// <summary>Runs the year-end close for a fiscal year.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The fiscal year to close.</param>
    /// <param name="retainedEarningsAccountCode">The equity account that receives the profit/loss.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The year-end result, or an error.</returns>
    Task<Result<YearEndResult>> RunAsync(long companyId, long fiscalYearId, string retainedEarningsAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Reverses a year-end that is not yet hard-locked: the closing journal is reversed in the period it was posted to and the year
    /// reopens (its periods stay closed until reopened), so adjustments can be made and the year-end run again.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The closed year.</param>
    /// <param name="reason">Why it is reversed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal journal number.</returns>
    Task<Result<string>> ReverseAsync(long companyId, long fiscalYearId, string reason, CancellationToken ct = default);

    /// <summary>Makes a closed year final: every period is permanently closed and the year-end can no longer be reversed.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The closed year.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> HardLockAsync(long companyId, long fiscalYearId, CancellationToken ct = default);
}
