using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A dunning notice for one overdue customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="Level">The dunning level (1 = first reminder, 2 = second, 3 = final notice).</param>
/// <param name="LevelLabel">A human label for the level.</param>
/// <param name="OverdueAmount">The total overdue balance.</param>
/// <param name="OldestDaysOverdue">Days the oldest overdue item is past due.</param>
public readonly record struct DunningNotice(long CustomerId, string CustomerName, int Level, string LevelLabel, decimal OverdueAmount, int OldestDaysOverdue);

/// <summary>
/// Assesses customers for dunning (overdue reminders): scans the AR ledger for overdue balances and, using
/// escalating day thresholds, assigns each overdue customer a reminder level with their overdue total and
/// oldest days outstanding — the basis for reminder letters and collection follow-up.
/// </summary>
public interface IDunningService
{
    /// <summary>Assesses overdue customers and returns a dunning notice per customer at level 1 or above.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="stages">The stage day thresholds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dunning notices, ordered by level then customer.</returns>
    Task<Result<IReadOnlyList<DunningNotice>>> AssessAsync(long companyId, DateOnly asAtDate, DunningStages stages, CancellationToken ct = default);
}
