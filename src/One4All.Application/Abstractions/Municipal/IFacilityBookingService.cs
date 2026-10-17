using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A request to book a facility.</summary>
public sealed class FacilityBookingRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The facility code.</summary>
    public string FacilityCode { get; init; } = string.Empty;

    /// <summary>The hirer's customer account.</summary>
    public long CustomerId { get; init; }

    /// <summary>The first day.</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>The last day.</summary>
    public DateOnly ToDate { get; init; }

    /// <summary>What it is for.</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>The date the booking is made and invoiced.</summary>
    public DateOnly BookedOn { get; init; }
}

/// <summary>A booking made.</summary>
/// <param name="BookingNumber">The booking and invoice number.</param>
/// <param name="Days">The days booked.</param>
/// <param name="Fee">The hire fee invoiced.</param>
public sealed record FacilityBooked(string BookingNumber, int Days, decimal Fee);

/// <summary>A booked range on a facility's calendar.</summary>
/// <param name="BookingNumber">The booking.</param>
/// <param name="CustomerCode">The hirer.</param>
/// <param name="FromDate">The first day.</param>
/// <param name="ToDate">The last day.</param>
/// <param name="Purpose">What it is for.</param>
public sealed record FacilityCalendarEntry(string BookingNumber, string CustomerCode, DateOnly FromDate, DateOnly ToDate, string Purpose);

/// <summary>
/// Facility hire (brief §4.9: halls, market stalls, sports grounds and other council revenue streams): register
/// facilities with a daily rate, book them for a range of days - never twice for the same day - invoicing the hire
/// through AR, cancel with a refund credited, and show a facility's calendar.
/// </summary>
public interface IFacilityBookingService
{
    /// <summary>Registers a facility.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The facility code (unique).</param>
    /// <param name="name">The facility name.</param>
    /// <param name="dailyRate">The hire charge per day.</param>
    /// <param name="revenueAccountCode">The revenue account hire is credited to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The facility, or why it was refused.</returns>
    Task<Result<long>> CreateFacilityAsync(long companyId, string code, string name, decimal dailyRate, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Books a facility and invoices the hire, in one unit; refused when any day is already booked.</summary>
    /// <param name="request">The booking.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The booking, or why it was refused.</returns>
    Task<Result<FacilityBooked>> BookAsync(FacilityBookingRequest request, CancellationToken ct = default);

    /// <summary>Cancels a booking, crediting a percentage of the fee to the hirer, in one unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="bookingNumber">The booking.</param>
    /// <param name="date">The cancellation date.</param>
    /// <param name="refundPercent">The percentage of the fee refunded (0–100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refund credited, or why it was refused.</returns>
    Task<Result<decimal>> CancelAsync(long companyId, string bookingNumber, DateOnly date, decimal refundPercent, CancellationToken ct = default);

    /// <summary>The bookings on a facility overlapping a period, in date order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="facilityCode">The facility.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calendar, or why there is none.</returns>
    Task<Result<IReadOnlyList<FacilityCalendarEntry>>> CalendarAsync(long companyId, string facilityCode, DateOnly start, DateOnly end, CancellationToken ct = default);
}
