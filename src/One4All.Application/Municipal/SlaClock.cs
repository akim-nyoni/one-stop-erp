using One4All.Core.Municipal;

namespace One4All.Application.Municipal;

/// <summary>Where a ticket stands against one SLA target.</summary>
public enum SlaState
{
    /// <summary>Met: done within the target.</summary>
    Met = 1,

    /// <summary>Still open and within the target.</summary>
    OnTrack = 2,

    /// <summary>Still open with 80% or more of the time gone.</summary>
    AtRisk = 3,

    /// <summary>Missed: done late, or still open past the target.</summary>
    Breached = 4,
}

/// <summary>
/// Service-desk SLA timing (brief §4.16): targets in round-the-clock hours from the report, shortened for urgent faults
/// (critical ×0.25, high ×0.5, normal ×1, low ×2), and each target's state — met, on track, at risk once 80% of the time is
/// gone, or breached. Pure and unit-tested.
/// </summary>
public static class SlaClock
{
    /// <summary>When a target falls due.</summary>
    /// <param name="reportedAt">When the fault was reported.</param>
    /// <param name="hours">The category's target in hours.</param>
    /// <param name="priority">The ticket's priority.</param>
    /// <returns>The due time.</returns>
    public static DateTimeOffset Due(DateTimeOffset reportedAt, decimal hours, TicketPriority priority)
    {
        decimal factor = priority switch
        {
            TicketPriority.Critical => 0.25m,
            TicketPriority.High => 0.5m,
            TicketPriority.Low => 2m,
            _ => 1m,
        };
        return reportedAt.AddMinutes((double)Math.Round(hours * factor * 60m, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>A target's state.</summary>
    /// <param name="reportedAt">When the fault was reported.</param>
    /// <param name="due">When the target falls due.</param>
    /// <param name="doneAt">When it was met (responded to / resolved), if it has been.</param>
    /// <param name="now">The time to judge at.</param>
    /// <returns>The state.</returns>
    public static SlaState State(DateTimeOffset reportedAt, DateTimeOffset due, DateTimeOffset? doneAt, DateTimeOffset now)
    {
        if (doneAt is DateTimeOffset done)
        {
            return done <= due ? SlaState.Met : SlaState.Breached;
        }

        if (now > due)
        {
            return SlaState.Breached;
        }

        double allowed = (due - reportedAt).TotalMinutes;
        return allowed > 0 && (now - reportedAt).TotalMinutes >= allowed * 0.8 ? SlaState.AtRisk : SlaState.OnTrack;
    }
}
