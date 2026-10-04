using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A block on a stand as the register shows it.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Reason">Why.</param>
/// <param name="Detail">The particulars.</param>
/// <param name="DocumentReference">The supporting document.</param>
/// <param name="PlacedByUserId">Who placed it.</param>
/// <param name="PlacedOn">When.</param>
/// <param name="LiftedByUserId">Who lifted it.</param>
/// <param name="LiftedOn">When it was lifted.</param>
/// <param name="LiftReason">Why it was lifted.</param>
public sealed record StandBlockEntry(string StandNumber, BlockReason Reason, string Detail, string DocumentReference, long PlacedByUserId, DateOnly PlacedOn,
    long? LiftedByUserId, DateOnly? LiftedOn, string? LiftReason);

/// <summary>
/// Caveats, disputes and blocks on stands (brief §4.10.6): a blocked stand cannot be allocated, sold, leased or
/// transferred; only an officer holding the caveat permission can place (Create) or lift (Delete) a block, each with a
/// reason and a supporting document, and the officer who placed a block cannot lift it.
/// </summary>
public interface IStandBlockService
{
    /// <summary>Blocks a stand.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="reason">Why.</param>
    /// <param name="detail">The particulars.</param>
    /// <param name="documentReference">The supporting document.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> PlaceAsync(long companyId, long standId, BlockReason reason, string detail, string documentReference, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Lifts the block in force on a stand.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="reason">Why.</param>
    /// <param name="documentReference">The supporting document.</param>
    /// <param name="userId">The officer (not the one who placed it).</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> LiftAsync(long companyId, long standId, string reason, string documentReference, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Every block ever placed on a stand, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blocks.</returns>
    Task<IReadOnlyList<StandBlockEntry>> HistoryAsync(long companyId, long standId, CancellationToken ct = default);

    /// <summary>The blocks in force, by stand number.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blocks.</returns>
    Task<IReadOnlyList<StandBlockEntry>> InForceAsync(long companyId, CancellationToken ct = default);
}
