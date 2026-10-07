using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A lodger permit application.</summary>
/// <param name="CompanyId">The council.</param>
/// <param name="TenancyNumber">The tenancy.</param>
/// <param name="LodgerName">The lodger.</param>
/// <param name="IdNumber">The lodger's national ID, if any.</param>
/// <param name="Rooms">Rooms let to the lodger.</param>
/// <param name="MonthlyFee">The monthly lodger fee billed with the rent (0: none).</param>
/// <param name="FeeAccountCode">The income account the fee goes to (needed with a fee).</param>
/// <param name="IssuedOn">When it starts.</param>
/// <param name="ExpiresOn">The last day it is in force.</param>
public sealed record LodgerPermitRequest(long CompanyId, string TenancyNumber, string LodgerName, string? IdNumber, int Rooms, decimal MonthlyFee, string? FeeAccountCode,
    DateOnly IssuedOn, DateOnly ExpiresOn);

/// <summary>Who lives in a let unit on a date.</summary>
/// <param name="TenancyNumber">The tenancy.</param>
/// <param name="UnitCode">The unit.</param>
/// <param name="Occupants">The household members living there.</param>
/// <param name="Lodgers">The lodgers under a permit in force.</param>
/// <param name="People">Everyone, the tenant included.</param>
/// <param name="MaxOccupants">The unit's limit (null: none set).</param>
public sealed record TenancyOccupancy(string TenancyNumber, string UnitCode, IReadOnlyList<string> Occupants, IReadOnlyList<string> Lodgers, int People, int? MaxOccupants);

/// <summary>
/// Who lives in council rental units (brief §4.10.7): the occupants list, lodgers only under a permit (the tenant keeps a room of their own, the unit's
/// occupancy limit holds, the permit's fee is billed with the rent), and the unit's occupancy limit.
/// </summary>
public interface IRentalOccupancyService
{
    /// <summary>Sets a unit's occupancy limit (null removes it).</summary>
    Task<Result> SetMaxOccupantsAsync(long companyId, string unitCode, int? maxOccupants, CancellationToken ct = default);

    /// <summary>Adds a household member to an active tenancy, within the unit's occupancy limit; returns their id.</summary>
    Task<Result<long>> AddOccupantAsync(long companyId, string tenancyNumber, string fullName, string? idNumber, string relationship, DateOnly movedInOn, CancellationToken ct = default);

    /// <summary>Records a household member moving out.</summary>
    Task<Result> RemoveOccupantAsync(long companyId, string tenancyNumber, long occupantId, DateOnly movedOutOn, CancellationToken ct = default);

    /// <summary>Issues a lodger permit; returns its number.</summary>
    Task<Result<string>> IssueLodgerPermitAsync(LodgerPermitRequest request, CancellationToken ct = default);

    /// <summary>Revokes a lodger permit from a date.</summary>
    Task<Result> RevokeLodgerPermitAsync(long companyId, string permitNumber, DateOnly lastDay, string reason, CancellationToken ct = default);

    /// <summary>Who lives in a tenancy's unit on a date.</summary>
    Task<Result<TenancyOccupancy>> OccupancyAsync(long companyId, string tenancyNumber, DateOnly date, CancellationToken ct = default);
}
