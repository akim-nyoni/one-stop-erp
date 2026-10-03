using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A promise to pay, for the diary.</summary>
/// <param name="PromiseId">The promise.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerCode">The customer's code.</param>
/// <param name="MadeOn">When it was made.</param>
/// <param name="PayBy">The date promised.</param>
/// <param name="Amount">The amount promised.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Note">The controller's note.</param>
public sealed record PromiseDiaryEntry(long PromiseId, long CustomerId, string CustomerCode, DateOnly MadeOn, DateOnly PayBy, decimal Amount, PromiseStatus Status, string? Note);

/// <summary>The outcome of reviewing open promises.</summary>
/// <param name="Kept">Promises found kept.</param>
/// <param name="Broken">Promises found broken, to chase.</param>
/// <param name="StillOpen">Promises not yet due or settled.</param>
public sealed record PromiseReview(IReadOnlyList<PromiseDiaryEntry> Kept, IReadOnlyList<PromiseDiaryEntry> Broken, int StillOpen);

/// <summary>
/// The promise-to-pay diary of the credit control workbench (brief §4.5): record a customer's promise, list the diary,
/// and review open promises against receipts. A customer with an open promise is held out of dunning until it is due.
/// </summary>
public interface IPromiseToPayService
{
    /// <summary>Records a promise; one open promise per customer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="madeOn">When it was made.</param>
    /// <param name="payBy">The date promised; not before it was made.</param>
    /// <param name="amount">The amount; positive.</param>
    /// <param name="note">A note, if any.</param>
    /// <param name="recordedByUserId">Who records it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The promise id, or why it was refused.</returns>
    Task<Result<long>> RecordAsync(long companyId, long customerId, DateOnly madeOn, DateOnly payBy, decimal amount, string? note, long recordedByUserId, CancellationToken ct = default);

    /// <summary>Lists the diary, optionally only promises in one state, by date promised.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="status">The state to list, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entries.</returns>
    Task<IReadOnlyList<PromiseDiaryEntry>> DiaryAsync(long companyId, PromiseStatus? status = null, CancellationToken ct = default);

    /// <summary>
    /// Reviews the open promises at a date: kept once receipts dated from the promise up to the promised date cover the
    /// amount, broken once the promised date has passed without them.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The review date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What the review found.</returns>
    Task<PromiseReview> ReviewAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
