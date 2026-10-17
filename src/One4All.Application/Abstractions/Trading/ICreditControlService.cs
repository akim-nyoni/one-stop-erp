using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Why a credit review placed (or would place) a customer on stop.</summary>
public enum CreditHoldReason
{
    /// <summary>The outstanding balance exceeds the approved credit limit.</summary>
    OverLimit = 1,

    /// <summary>Debt is overdue beyond the review's tolerance.</summary>
    Overdue = 2,

    /// <summary>Both over the limit and overdue.</summary>
    OverLimitAndOverdue = 3,
}

/// <summary>A customer the credit review placed on stop.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CustomerCode">The account reference.</param>
/// <param name="CustomerName">The customer name.</param>
/// <param name="Reason">Why they were held.</param>
/// <param name="Balance">The outstanding balance (base currency).</param>
/// <param name="CreditLimit">The credit limit (0 = unlimited).</param>
/// <param name="OverdueAmount">Outstanding debt past the tolerance (base currency).</param>
/// <param name="OldestOverdueDays">Days past due of the oldest overdue item.</param>
public sealed record CreditHold(long CustomerId, string CustomerCode, string CustomerName, CreditHoldReason Reason, decimal Balance, decimal CreditLimit, decimal OverdueAmount, int OldestOverdueDays);

/// <summary>The outcome of a credit review run.</summary>
/// <param name="AsAt">The review date.</param>
/// <param name="OverdueToleranceDays">Days past due a debt may be before it triggers a hold.</param>
/// <param name="Reviewed">Customers examined (active, not already on hold).</param>
/// <param name="Held">Customers newly placed on stop.</param>
public sealed record CreditReviewResult(DateOnly AsAt, int OverdueToleranceDays, int Reviewed, IReadOnlyList<CreditHold> Held);

/// <summary>
/// Credit control (brief §4.5): the periodic review that places customers on stop, and the controlled
/// release. A held customer is refused new sales orders and invoices until released; the review never
/// releases anyone - that is a deliberate human decision.
/// </summary>
public interface ICreditControlService
{
    /// <summary>
    /// Reviews every active customer not already on hold and places on stop those whose outstanding
    /// balance exceeds their credit limit (when one is set) or who have debt overdue by more than the
    /// tolerance. Safe to re-run: customers already on hold are skipped.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The review date (overdue is measured against this).</param>
    /// <param name="overdueToleranceDays">Days past due a debt may be before it triggers a hold (0 = any overdue).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The review outcome.</returns>
    Task<Result<CreditReviewResult>> ReviewAsync(long companyId, DateOnly asAt, int overdueToleranceDays, CancellationToken ct = default);

    /// <summary>Places a customer on stop by hand (e.g. on a dispute). Requires a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> HoldAsync(long companyId, long customerId, string reason, CancellationToken ct = default);

    /// <summary>Releases a customer from stop. Requires a reason; refuses a customer not on hold.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> ReleaseAsync(long companyId, long customerId, string reason, CancellationToken ct = default);
}
