using One4All.Licensing.Core;

namespace One4All.Application.Licensing;

/// <summary>When the desktop reminds the user about the licence (brief §11.5: "a daily reminder from 7 days before expiry"). Pure.</summary>
public static class LicenceReminder
{
    /// <summary>The days before expiry the daily reminder starts.</summary>
    public const int ReminderDays = 7;

    /// <summary>Whether to remind today: once a day, from seven days before expiry, and every day the licence is in grace, read-only or unusable.</summary>
    /// <param name="status">The licence status.</param>
    /// <param name="daysUntilExpiry">Days to expiry (null: perpetual).</param>
    /// <param name="lastShown">When the reminder was last shown (null: never).</param>
    /// <param name="today">Today.</param>
    /// <returns>True to show it.</returns>
    public static bool ShouldRemind(LicenseStatus status, int? daysUntilExpiry, DateOnly? lastShown, DateOnly today)
    {
        if (lastShown == today)
        {
            return false;
        }

        return status != LicenseStatus.Valid || daysUntilExpiry is int days && days <= ReminderDays;
    }
}
