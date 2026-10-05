using One4All.Core.Common;

namespace One4All.Application.JobCosting;

/// <summary>A haulage band, for charging.</summary>
/// <param name="Code">The zone code.</param>
/// <param name="FromKm">Where it starts (inclusive).</param>
/// <param name="ToKm">Where it ends (exclusive).</param>
/// <param name="RatePerTonne">The rate per tonne.</param>
/// <param name="MinimumCharge">The minimum per load.</param>
public readonly record struct HaulageBand(string Code, decimal FromKm, decimal ToKm, decimal RatePerTonne, decimal MinimumCharge);

/// <summary>Weighbridge arithmetic and controls.</summary>
public static class WeighbridgeRules
{
    /// <summary>The haulage charge for a load: the band the distance falls in, its rate times the tonnes, never below its minimum.</summary>
    /// <param name="bands">The zones.</param>
    /// <param name="distanceKm">The distance.</param>
    /// <param name="tonnes">The net tonnes.</param>
    /// <returns>The zone and charge, or null when no zone covers the distance.</returns>
    public static (string Zone, decimal Charge)? Haulage(IEnumerable<HaulageBand> bands, decimal distanceKm, decimal tonnes)
    {
        HaulageBand? band = bands.Where(b => distanceKm >= b.FromKm && distanceKm < b.ToKm).OrderBy(b => b.FromKm).Cast<HaulageBand?>().FirstOrDefault();
        return band is { } z ? (z.Code, Math.Max(z.MinimumCharge, Math.Round(z.RatePerTonne * tonnes, 2, MidpointRounding.AwayFromZero))) : null;
    }

    /// <summary>Checks a weight before it is recorded.</summary>
    /// <param name="weightKg">The weight.</param>
    /// <param name="isManual">Whether it was keyed by hand.</param>
    /// <param name="reason">Why it was keyed.</param>
    /// <param name="operatorUserId">The operator.</param>
    /// <param name="authorisedByUserId">The supervisor.</param>
    /// <returns>Null when acceptable; otherwise the error.</returns>
    public static Error? ValidateWeight(decimal weightKg, bool isManual, string? reason, long? operatorUserId, long? authorisedByUserId)
    {
        if (weightKg <= 0m)
        {
            return Error.Validation("Wb.Weight", "A weight must be positive.");
        }

        if (!isManual)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Wb.ManualReason", "A manually keyed weight needs a reason.");
        }

        if (authorisedByUserId is null || authorisedByUserId == operatorUserId)
        {
            return Error.Rule("Wb.ManualAuthorisation", "A manually keyed weight needs a supervisor other than the operator.");
        }

        return null;
    }

    /// <summary>Minutes between time in and time out.</summary>
    /// <param name="timeIn">Time in.</param>
    /// <param name="timeOut">Time out.</param>
    /// <returns>The whole minutes, or null when either is missing.</returns>
    public static int? TurnaroundMinutes(DateTimeOffset? timeIn, DateTimeOffset? timeOut)
        => timeIn is { } a && timeOut is { } b ? (int)Math.Round((b - a).TotalMinutes, MidpointRounding.AwayFromZero) : null;
}
