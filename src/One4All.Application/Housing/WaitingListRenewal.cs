using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>What the renewal run should do to an applicant.</summary>
public enum RenewalAction
{
    /// <summary>Nothing: confirmed recently enough.</summary>
    None = 0,

    /// <summary>Suspend: the renewal is overdue.</summary>
    Suspend = 1,

    /// <summary>Remove: still unconfirmed after the grace period.</summary>
    Remove = 2,

    /// <summary>Remind: the renewal falls due soon and the applicant has not been reminded this cycle.</summary>
    Remind = 3,
}

/// <summary>
/// The waiting-list renewal cycle (brief §4.10.3: periodic re-registration; non-responders reminded, suspended, then removed after
/// a grace period, each step notified). Pure and unit-tested.
/// </summary>
public static class WaitingListRenewal
{
    /// <summary>What the renewal run should do to an applicant on a date.</summary>
    /// <param name="status">The applicant's status.</param>
    /// <param name="lastConfirmed">When the applicant last confirmed (the registration date if never).</param>
    /// <param name="asAtDate">The run date.</param>
    /// <param name="cycleMonths">How often applicants must confirm.</param>
    /// <param name="graceDays">Days a suspension stands, notified, before the applicant is removed.</param>
    /// <param name="suspendedOn">When the suspension notice went out (null: not on record, so the grace runs from the due date).</param>
    /// <param name="remindedThisCycle">Whether a renewal-due reminder has gone out since the last confirmation.</param>
    /// <param name="reminderDays">How many days before the renewal falls due the reminder goes out.</param>
    /// <returns>The action.</returns>
    /// <remarks>
    /// Flagged, then suspended, then removed (brief §4.10.3): an overdue applicant is always suspended - and told - first, never removed in one step, so the notices
    /// prove they were contacted before they lost their place.
    /// </remarks>
    public static RenewalAction Assess(ApplicantStatus status, DateOnly lastConfirmed, DateOnly asAtDate, int cycleMonths, int graceDays, DateOnly? suspendedOn = null,
        bool remindedThisCycle = false, int reminderDays = 30)
    {
        DateOnly due = lastConfirmed.AddMonths(cycleMonths);
        return status switch
        {
            ApplicantStatus.Active when asAtDate > due => RenewalAction.Suspend,
            ApplicantStatus.Active when !remindedThisCycle && asAtDate >= due.AddDays(-Math.Max(0, reminderDays)) => RenewalAction.Remind,
            ApplicantStatus.Suspended when (suspendedOn is DateOnly since ? asAtDate >= since.AddDays(graceDays) : asAtDate > due.AddDays(graceDays)) => RenewalAction.Remove,
            _ => RenewalAction.None,
        };
    }
}
