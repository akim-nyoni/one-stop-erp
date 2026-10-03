using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A request to raise a credit limit, waiting for approval.</summary>
/// <param name="RequestId">The request.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerCode">The customer's code.</param>
/// <param name="CurrentLimit">The limit now (0 = unlimited).</param>
/// <param name="RequestedLimit">The limit asked for (0 = unlimited).</param>
/// <param name="Reason">Why.</param>
/// <param name="RequestedByUserId">Who asked; they cannot approve it.</param>
public sealed record PendingCreditLimit(long RequestId, long CustomerId, string CustomerCode, decimal CurrentLimit, decimal RequestedLimit, string Reason, long RequestedByUserId);

/// <summary>
/// The credit limit approval workflow (brief §4.5): lowering a limit takes effect at once; raising one (or removing it)
/// is requested by one user and applied only when a different user approves it.
/// </summary>
public interface ICreditLimitService
{
    /// <summary>Lowers a customer's limit at once.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="limit">The new, lower limit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused (it would not lower the limit).</returns>
    Task<Result> LowerAsync(long companyId, long customerId, decimal limit, CancellationToken ct = default);

    /// <summary>Requests a higher limit, or none (0); one request at a time per customer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="limit">The limit asked for.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request id, or why it was refused.</returns>
    Task<Result<long>> RequestIncreaseAsync(long companyId, long customerId, decimal limit, string reason, long requestedByUserId, CancellationToken ct = default);

    /// <summary>The requests waiting for approval, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requests.</returns>
    Task<IReadOnlyList<PendingCreditLimit>> PendingAsync(long companyId, CancellationToken ct = default);

    /// <summary>Approves a request and raises the limit; never by the requester.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, long requestId, long approverUserId, CancellationToken ct = default);

    /// <summary>Rejects a request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="userId">Who rejects it.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, long requestId, long userId, string reason, CancellationToken ct = default);
}
