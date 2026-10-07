using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>One line change in a sales order amendment.</summary>
/// <param name="SalesOrderLineId">The line.</param>
/// <param name="Quantity">The new ordered quantity.</param>
/// <param name="UnitPrice">The new unit price.</param>
public sealed record SalesOrderLineChange(long SalesOrderLineId, decimal Quantity, decimal UnitPrice);

/// <summary>An amendment.</summary>
/// <param name="AmendedOn">When.</param>
/// <param name="UserId">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="Lines">The line changes.</param>
public sealed record SalesOrderAmendment(DateOnly AmendedOn, long UserId, string Reason, IReadOnlyList<SalesOrderLineChange> Lines);

/// <summary>An amendment made.</summary>
/// <param name="Version">The order's new version.</param>
/// <param name="TotalBefore">Its value before.</param>
/// <param name="TotalAfter">Its value after.</param>
public sealed record SalesOrderAmendmentResult(int Version, decimal TotalBefore, decimal TotalAfter);

/// <summary>One version in an order's history.</summary>
/// <param name="Version">The version.</param>
/// <param name="TotalNet">Its value.</param>
/// <param name="Lines">Its lines (line id, quantity, price).</param>
/// <param name="Reason">Why it was replaced (null for the current one).</param>
/// <param name="AmendedByUserId">Who replaced it.</param>
/// <param name="AmendedOn">When.</param>
public sealed record SalesOrderVersion(int Version, decimal TotalNet, IReadOnlyList<SalesOrderLineChange> Lines, string? Reason, long? AmendedByUserId, DateOnly? AmendedOn);

/// <summary>
/// Sales order amendments with history (brief §4.13): quantities and prices change as a new version, the earlier one kept with the reason;
/// nothing goes below what has been despatched or invoiced and invoiced prices do not change.
/// </summary>
public interface ISalesOrderAmendmentService
{
    /// <summary>Amends an order.</summary>
    Task<Result<SalesOrderAmendmentResult>> AmendAsync(long companyId, long salesOrderId, SalesOrderAmendment amendment, CancellationToken ct = default);

    /// <summary>The order's versions, oldest first, ending with the current one.</summary>
    Task<Result<IReadOnlyList<SalesOrderVersion>>> HistoryAsync(long companyId, long salesOrderId, CancellationToken ct = default);
}
