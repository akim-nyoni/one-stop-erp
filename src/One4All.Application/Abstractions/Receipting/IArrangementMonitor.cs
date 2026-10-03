using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Receipting;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>An arrangement's standing after a review.</summary>
/// <param name="ArrangementId">The arrangement.</param>
/// <param name="CustomerCode">The debtor.</param>
/// <param name="Standing">Due, paid, shortfall, missed instalments and the next due.</param>
/// <param name="Status">Its status after the review.</param>
public sealed record ArrangementReview(long ArrangementId, string CustomerCode, ArrangementStanding Standing, ArrangementStatus Status);

/// <summary>
/// Payment arrangement schedules and default tracking (brief §4.9 debt management): the instalment schedule of an
/// arrangement, and a review that measures receipts since each active arrangement started against its schedule, marking
/// it honoured once paid or defaulted once enough instalments are missed.
/// </summary>
public interface IArrangementMonitor
{
    /// <summary>The instalment schedule of an arrangement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="arrangementId">The arrangement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schedule, or why there is none.</returns>
    Task<Result<IReadOnlyList<Instalment>>> ScheduleAsync(long companyId, long arrangementId, CancellationToken ct = default);

    /// <summary>Reviews every active arrangement as at a date and updates the honoured and defaulted ones.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The review date.</param>
    /// <param name="graceDays">Days after the due date before an instalment counts as missed.</param>
    /// <param name="missedToDefault">Missed instalments that default an arrangement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each active arrangement's standing.</returns>
    Task<IReadOnlyList<ArrangementReview>> ReviewAsync(long companyId, DateOnly asAtDate, int graceDays, int missedToDefault, CancellationToken ct = default);
}
