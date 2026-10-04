using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// The eviction workflow's rules (brief §4.10.7: reminder → demand → notice to vacate → legal handover → court order →
/// eviction): stages strictly in order and never back-dated, the notice period honoured before legal handover, and a
/// court-order reference required for the court order. Pure and unit-tested.
/// </summary>
public static class EvictionWorkflow
{
    /// <summary>Why a stage cannot be recorded now; null when it can.</summary>
    /// <param name="current">The latest stage reached.</param>
    /// <param name="currentDate">When it was reached.</param>
    /// <param name="noticeDate">When notice to vacate was given, if it has been.</param>
    /// <param name="noticePeriodDays">Days between notice to vacate and legal handover.</param>
    /// <param name="requested">The stage to record.</param>
    /// <param name="date">Its date.</param>
    /// <param name="reference">Its reference.</param>
    /// <returns>The problem, or null.</returns>
    public static string? Problem(EvictionStage current, DateOnly currentDate, DateOnly? noticeDate, int noticePeriodDays, EvictionStage requested, DateOnly date, string? reference)
    {
        if (current == EvictionStage.Evicted)
        {
            return "The tenant has already been evicted.";
        }

        if ((int)requested != (int)current + 1)
        {
            return $"After {current} the next stage is {(EvictionStage)((int)current + 1)}, not {requested}.";
        }

        if (date < currentDate)
        {
            return $"{requested} cannot be dated before {current} ({currentDate:yyyy-MM-dd}).";
        }

        if (requested == EvictionStage.LegalHandover && noticeDate is DateOnly notice && date < notice.AddDays(noticePeriodDays))
        {
            return $"The {noticePeriodDays}-day notice to vacate runs until {notice.AddDays(noticePeriodDays):yyyy-MM-dd}.";
        }

        if (requested == EvictionStage.CourtOrder && string.IsNullOrWhiteSpace(reference))
        {
            return "A court order needs its reference.";
        }

        return null;
    }
}
