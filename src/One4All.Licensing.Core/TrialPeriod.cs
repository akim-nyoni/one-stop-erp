namespace One4All.Licensing.Core;

/// <summary>
/// A trial counted from first activation (brief §11.1: "time-limited trial from first activation"): the vendor issues one signed Trial licence carrying
/// the <c>trial_days</c> limit, and each installation may use it for that many days from the day it was first installed there — after which it is
/// read-only (data can still be seen and exported) until a full licence is installed.
/// </summary>
public static class TrialPeriod
{
    /// <summary>The licence limit holding the trial's length in days.</summary>
    public const string DaysLimit = "trial_days";

    /// <summary>When a trial licence ends on this installation, or null when the licence is not a trial counted from activation.</summary>
    /// <param name="payload">The licence.</param>
    /// <param name="firstActivated">When it was first installed here.</param>
    /// <returns>The last day of the trial.</returns>
    public static DateOnly? EndsOn(LicensePayload? payload, DateTimeOffset firstActivated)
        => payload is { LicenceType: LicenceType.Trial } && payload.Limits.TryGetValue(DaysLimit, out int? days) && days is int length and > 0
            ? DateOnly.FromDateTime(firstActivated.UtcDateTime).AddDays(length - 1)
            : null;

    /// <summary>Applies the trial's end to a validation: a usable trial past its days becomes read-only.</summary>
    /// <param name="result">The signature / dates validation.</param>
    /// <param name="firstActivated">When it was first installed here.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The result, read-only once the trial is over.</returns>
    public static LicenseValidationResult Apply(LicenseValidationResult result, DateTimeOffset firstActivated, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(clock);
        if (!result.IsUsable || EndsOn(result.Payload, firstActivated) is not DateOnly ends)
        {
            return result;
        }

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return today > ends
            ? LicenseValidationResult.ForPayload(LicenseStatus.ReadOnly, result.Payload!, $"The trial ended on {ends:yyyy-MM-dd}: install a licence to carry on working.", clock)
            : LicenseValidationResult.ForPayload(result.Status, result.Payload!, $"Trial: {ends.DayNumber - today.DayNumber + 1} day(s) left (to {ends:yyyy-MM-dd}).", clock);
    }
}
