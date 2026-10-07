using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A stand's audited status move.</summary>
/// <param name="From">Where it was.</param>
/// <param name="To">Where it went.</param>
/// <param name="Date">When.</param>
/// <param name="UserId">Who (null when a document moved it).</param>
/// <param name="Authority">On what authority.</param>
/// <param name="Reason">Why.</param>
public sealed record StandMove(StandStatus From, StandStatus To, DateOnly Date, long? UserId, string Authority, string? Reason);

/// <summary>A stand's history: where it is, where it can go, every move, and its lineage back to the parcel.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Next">Where it can go.</param>
/// <param name="Moves">Every move, oldest first.</param>
/// <param name="Lineage">The stands it came from (re-subdivided from / consolidated from), nearest first, ending at the parcel.</param>
public sealed record StandHistory(string StandNumber, StandStatus Status, IReadOnlyList<StandStatus> Next, IReadOnlyList<StandMove> Moves, IReadOnlyList<string> Lineage);

/// <summary>One new stand of a re-subdivision.</summary>
/// <param name="StandNumber">Its number.</param>
/// <param name="SizeSquareMetres">Its size.</param>
public readonly record struct StandPart(string StandNumber, decimal SizeSquareMetres);

/// <summary>
/// The stand status state machine (brief §4.10.2): manual moves along the lifecycle with their authority (no state jumped; allocation, sale,
/// cession and repossession move stands through their own documents), holds and releases, consolidation of stands into one and
/// re-subdivision of a stand, each keeping lineage back to the parcel.
/// </summary>
public interface IStandLifecycleService
{
    /// <summary>Moves a stand to its next state (or withholds it, or releases a hold back to where it was).</summary>
    Task<Result<StandStatus>> MoveAsync(long companyId, string standNumber, StandStatus to, DateOnly date, long userId, string authority, string? reason, CancellationToken ct = default);

    /// <summary>Consolidates unencumbered stands of one scheme into a new stand (sizes and costs added up).</summary>
    Task<Result<string>> ConsolidateAsync(long companyId, IReadOnlyList<string> standNumbers, string newStandNumber, DateOnly date, long userId, string authority, CancellationToken ct = default);

    /// <summary>Re-subdivides an unencumbered stand into new stands (sizes must add up; cost split by area).</summary>
    Task<Result<IReadOnlyList<string>>> ResubdivideAsync(long companyId, string standNumber, IReadOnlyList<StandPart> parts, DateOnly date, long userId, string authority, CancellationToken ct = default);

    /// <summary>A stand's history and lineage.</summary>
    Task<Result<StandHistory>> HistoryAsync(long companyId, string standNumber, CancellationToken ct = default);
}
