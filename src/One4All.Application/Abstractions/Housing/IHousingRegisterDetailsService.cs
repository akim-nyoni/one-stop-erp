using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A stand's register details (blank values clear the field).</summary>
public sealed record StandDetails(string? StreetAddress, string? Ward, string? Suburb, string? Zone, string? IntendedUse, string? Zoning, string? ServicingPhase,
    bool HasWater, bool HasSewer, bool HasRoad, bool HasElectricity, bool HasStormWater);

/// <summary>A parcel's register details.</summary>
public sealed record ParcelDetails(string? Locality, string? Ward, string? LandUse, ParcelTenure? Tenure, string? Improvements, ParcelOccupation? Occupation);

/// <summary>A scheme's funding and dates.</summary>
public sealed record SchemeDetails(SchemeFunding? FundingSource, DateOnly? StartDate, DateOnly? CompletionDate);

/// <summary>
/// The register details the brief asks the stand, parcel and scheme records to carry (§4.10.1–4.10.2): a stand's address, ward / suburb / zone, intended use,
/// zoning, servicing phase, service availability and remarks history; a parcel's locality, ward, land use, tenure, improvements and occupation; a scheme's
/// funding source and dates.
/// </summary>
public interface IHousingRegisterDetailsService
{
    /// <summary>Sets a stand's details.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="details">The details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetStandDetailsAsync(long companyId, string standNumber, StandDetails details, CancellationToken ct = default);

    /// <summary>Adds a remark to a stand's history.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="text">The remark.</param>
    /// <param name="recordedBy">Who recorded it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AddStandRemarkAsync(long companyId, string standNumber, string text, string recordedBy, CancellationToken ct = default);

    /// <summary>A stand's remarks, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remarks (when, who, what).</returns>
    Task<IReadOnlyList<(DateTimeOffset At, string RecordedBy, string Text)>> StandRemarksAsync(long companyId, string standNumber, CancellationToken ct = default);

    /// <summary>Sets a parcel's details.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="parcelNumber">The parcel.</param>
    /// <param name="details">The details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetParcelDetailsAsync(long companyId, string parcelNumber, ParcelDetails details, CancellationToken ct = default);

    /// <summary>Sets a scheme's funding and dates (the completion not before the start).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeCode">The scheme.</param>
    /// <param name="details">The details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetSchemeDetailsAsync(long companyId, string schemeCode, SchemeDetails details, CancellationToken ct = default);
}
