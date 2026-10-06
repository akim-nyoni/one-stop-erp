using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>An item on a contract to create.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="AgreedQuantity">The quantity agreed.</param>
/// <param name="UnitPrice">The agreed price.</param>
public readonly record struct ContractLineRequest(long ItemId, decimal AgreedQuantity, decimal UnitPrice);

/// <summary>An item to call off.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
public readonly record struct CallOffLine(long ItemId, decimal Quantity);

/// <summary>A contract line's position.</summary>
/// <param name="ItemCode">The item.</param>
/// <param name="Agreed">Agreed quantity.</param>
/// <param name="CalledOff">Called off so far.</param>
/// <param name="Remaining">Still to call off.</param>
/// <param name="UnitPrice">The agreed price.</param>
public readonly record struct ContractPosition(string ItemCode, decimal Agreed, decimal CalledOff, decimal Remaining, decimal UnitPrice);

/// <summary>
/// Customer contracts and call-off orders (brief §4.13): agreed quantities at agreed prices for a period; each call-off raises a sales
/// order at the contract prices and draws the contract down, never past what was agreed or outside its dates.
/// </summary>
public interface ICustomerContractService
{
    /// <summary>Creates a contract (<c>Sop.ContractDuplicate</c> for a number in use; positive quantities, non-negative prices).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="startDate">The first day.</param>
    /// <param name="endDate">The last day.</param>
    /// <param name="lines">The items.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The contract id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long customerId, string contractNumber, DateOnly startDate, DateOnly endDate, IReadOnlyList<ContractLineRequest> lines, CancellationToken ct = default);

    /// <summary>
    /// Calls off against a contract: a sales order for the customer at the contract prices (the items' revenue account and tax code).
    /// Refused outside the contract dates (<c>Sop.ContractNotActive</c>), for an item not on it (<c>Sop.NotOnContract</c>) or beyond
    /// what remains (<c>Sop.ContractExceeded</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractNumber">The contract.</param>
    /// <param name="orderNumber">The order number (blank to number it automatically).</param>
    /// <param name="date">The order date.</param>
    /// <param name="lines">What to call off.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sales order.</returns>
    Task<Result<SalesOrderResult>> CallOffAsync(long companyId, string contractNumber, string orderNumber, DateOnly date, IReadOnlyList<CallOffLine> lines, CancellationToken ct = default);

    /// <summary>A contract's lines: agreed, called off and remaining.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractNumber">The contract.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The position.</returns>
    Task<Result<IReadOnlyList<ContractPosition>>> PositionAsync(long companyId, string contractNumber, CancellationToken ct = default);
}
