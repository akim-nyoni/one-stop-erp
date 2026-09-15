using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The result of a year-end close.</summary>
/// <param name="ClosingJournalNumber">The number of the P&amp;L closing journal.</param>
/// <param name="NetProfitClosed">The net profit rolled to retained earnings.</param>
/// <param name="NextYearId">The id of the newly created (or existing) next fiscal year.</param>
public readonly record struct YearEndResult(string ClosingJournalNumber, decimal NetProfitClosed, long NextYearId);

/// <summary>
/// Runs the year-end routine (brief §3.4): validates the trial balance, rolls the profit or loss to
/// retained earnings via a closing journal, opens the next year, and permanently closes the prior year.
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
}
