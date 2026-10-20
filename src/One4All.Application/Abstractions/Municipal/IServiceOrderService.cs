using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A property whose arrears call for disconnection.</summary>
/// <param name="PropertyId">The property.</param>
/// <param name="StandNumber">Its stand number.</param>
/// <param name="Arrears">The overdue balance.</param>
/// <param name="OldestDueDate">The oldest unpaid due date.</param>
public sealed record DisconnectionCandidate(long PropertyId, string StandNumber, decimal Arrears, DateOnly OldestDueDate);

/// <summary>The reconnection fee to charge.</summary>
/// <param name="Amount">The fee.</param>
/// <param name="RevenueAccountCode">Where the fee income goes.</param>
public sealed record ReconnectionFee(decimal Amount, string RevenueAccountCode);

/// <summary>
/// The disconnection / reconnection workflow (brief §4.9 debt management): list properties whose overdue balance passes
/// the threshold, raise disconnection orders for them and carry them out (the service stops), then raise reconnection
/// orders once the arrears are back under the threshold and carry them out (the service resumes and the reconnection fee
/// is invoiced). An open order for the same service blocks another.
/// </summary>
public interface IServiceOrderService
{
    /// <summary>Properties with an active service whose overdue balance is at least the threshold.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="serviceType">The service to cut.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="threshold">The overdue balance that triggers disconnection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The candidates, largest arrears first.</returns>
    Task<IReadOnlyList<DisconnectionCandidate>> CandidatesAsync(long companyId, ServiceType serviceType, DateOnly asAtDate, decimal threshold, CancellationToken ct = default);

    /// <summary>Raises an order for a property's service.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="kind">Disconnect or reconnect.</param>
    /// <param name="date">The date.</param>
    /// <param name="threshold">The arrears threshold: disconnection needs at least this overdue, reconnection less.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order number, or why it was refused.</returns>
    Task<Result<string>> RaiseAsync(long companyId, long propertyId, ServiceType serviceType, ServiceOrderKind kind, DateOnly date, decimal threshold, CancellationToken ct = default);

    /// <summary>Authorises an open disconnection: by a user other than whoever raised it, before it can be carried out.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="orderNumber">The order.</param>
    /// <param name="userId">The authorising user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AuthoriseAsync(long companyId, string orderNumber, long userId, CancellationToken ct = default);

    /// <summary>Carries out an open order (a disconnection once authorised); its fee - disconnection or reconnection - is invoiced, in one unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="orderNumber">The order.</param>
    /// <param name="date">When it was carried out.</param>
    /// <param name="fee">The disconnection or reconnection fee, if one is charged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ExecuteAsync(long companyId, string orderNumber, DateOnly date, ReconnectionFee? fee, CancellationToken ct = default);

    /// <summary>Cancels an open order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="orderNumber">The order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CancelAsync(long companyId, string orderNumber, CancellationToken ct = default);
}
