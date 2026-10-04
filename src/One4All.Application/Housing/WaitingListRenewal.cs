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
}

/// <summary>
/// The waiting-list renewal cycle (brief §4.10.3: periodic re-registration; non-responders suspended, then removed after
/// a grace period). Pure and unit-tested.
/// </summary>
public static class WaitingListRenewal
{
    /// <summary>What the renewal run should do to an applicant on a date.</summary>
    /// <param name="status">The applicant's status.</param>
    /// <param name="lastConfirmed">When the applicant last confirmed (the registration date if never).</param>
    /// <param name="asAtDate">The run date.</param>
    /// <param name="cycleMonths">How often applicants must confirm.</param>
    /// <param name="graceDays">Days after the renewal falls due before a suspended applicant is removed.</param>
    /// <returns>The action.</returns>
    public static RenewalAction Assess(ApplicantStatus status, DateOnly lastConfirmed, DateOnly asAtDate, int cycleMonths, int graceDays)
    {
        DateOnly due = lastConfirmed.AddMonths(cycleMonths);
        return status switch
        {
            ApplicantStatus.Active when asAtDate > due => asAtDate > due.AddDays(graceDays) ? RenewalAction.Remove : RenewalAction.Suspend,
            ApplicantStatus.Suspended when asAtDate > due.AddDays(graceDays) => RenewalAction.Remove,
            _ => RenewalAction.None,
        };
    }
}
