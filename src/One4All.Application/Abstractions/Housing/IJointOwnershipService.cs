using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A holder on a date.</summary>
/// <param name="CustomerId">The holder's account.</param>
/// <param name="Name">The holder.</param>
/// <param name="SharePercent">The share.</param>
/// <param name="HeldFrom">Held since.</param>
/// <param name="Deceased">Whether the holder has died (the share sits with the estate).</param>
public sealed record StandHolderEntry(long CustomerId, string Name, decimal SharePercent, DateOnly HeldFrom, bool Deceased);

/// <summary>
/// Joint ownership of stands (brief §4.10.6): holders with shares, each their own account; transfer of one share only
/// (which severs a joint tenancy); survivorship on a death; and the holders on any date from the append-only history.
/// </summary>
public interface IJointOwnershipService
{
    /// <summary>Registers a sold stand's joint holders; the current holder must be among them.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="regime">How they hold it.</param>
    /// <param name="holders">The holders and shares (totalling 100%).</param>
    /// <param name="date">From when.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RegisterAsync(long companyId, long standId, CoOwnershipRegime regime, IReadOnlyList<HolderShare> holders, DateOnly date, CancellationToken ct = default);

    /// <summary>Transfers part or all of one holder's share; a joint tenancy is severed into a tenancy in common.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="fromCustomerId">The holder parting with a share.</param>
    /// <param name="toCustomerId">The recipient.</param>
    /// <param name="portion">The share transferred (percentage points).</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> TransferShareAsync(long companyId, long standId, long fromCustomerId, long toCustomerId, decimal portion, DateOnly date, CancellationToken ct = default);

    /// <summary>Records a holder's death: survivorship under joint tenancy, the estate's share under tenancy in common.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="deceasedCustomerId">The holder who died.</param>
    /// <param name="date">The date of death.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The holders after, or why it was refused.</returns>
    Task<Result<IReadOnlyList<StandHolderEntry>>> RecordDeathAsync(long companyId, long standId, long deceasedCustomerId, DateOnly date, CancellationToken ct = default);

    /// <summary>The holders on a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The holders, largest share first.</returns>
    Task<IReadOnlyList<StandHolderEntry>> HoldersAsync(long companyId, long standId, DateOnly asAt, CancellationToken ct = default);
}
