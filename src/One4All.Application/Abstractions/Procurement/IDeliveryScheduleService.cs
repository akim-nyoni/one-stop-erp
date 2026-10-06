using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>A delivery that is late or due soon.</summary>
/// <param name="OrderNumber">The purchase order.</param>
/// <param name="SupplierCode">The supplier.</param>
/// <param name="ItemCode">The item.</param>
/// <param name="DueDate">When it is due.</param>
/// <param name="Scheduled">The quantity due.</param>
/// <param name="Received">Received against it.</param>
/// <param name="Outstanding">Still to come.</param>
/// <param name="DaysLate">Days past due (negative: due in that many days).</param>
public sealed record ExpediteLine(string OrderNumber, string SupplierCode, string ItemCode, DateOnly DueDate, decimal Scheduled, decimal Received, decimal Outstanding, int DaysLate);

/// <summary>Delivery schedules on purchase order lines and the expediting list (brief §4.3).</summary>
public interface IDeliveryScheduleService
{
    /// <summary>
    /// Replaces a line's delivery schedule. The deliveries must add up to the line's ordered quantity and fall on or after the order date;
    /// an order that is cancelled or closed cannot be rescheduled.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="deliveries">Each due date and quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ScheduleAsync(long companyId, long purchaseOrderLineId, IReadOnlyList<(DateOnly Due, decimal Quantity)> deliveries, CancellationToken ct = default);

    /// <summary>
    /// The expediting list: every outstanding delivery on open orders due by the horizon (the date plus the days ahead), most late first.
    /// Unscheduled lines are due on the order's required-by date, or its order date.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asOf">The date.</param>
    /// <param name="daysAhead">How far ahead to include deliveries due soon.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deliveries to chase.</returns>
    Task<IReadOnlyList<ExpediteLine>> ExpediteAsync(long companyId, DateOnly asOf, int daysAhead = 0, CancellationToken ct = default);
}
