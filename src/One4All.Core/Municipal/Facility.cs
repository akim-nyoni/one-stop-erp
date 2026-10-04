using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>
/// A council facility let out by the day (<c>mun.facility</c>, brief §4.9): a hall, sports ground, market stall,
/// chapel or similar, with a daily rate and the revenue account its hire is credited to.
/// </summary>
public sealed class Facility : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Facility()
    {
        Code = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a facility.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The facility code.</param>
    /// <param name="name">The facility name.</param>
    /// <param name="dailyRate">The hire charge per day.</param>
    /// <param name="revenueAccountCode">The revenue account hire is credited to.</param>
    public Facility(long companyId, string code, string name, decimal dailyRate, string revenueAccountCode)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        DailyRate = dailyRate;
        RevenueAccountCode = revenueAccountCode;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The facility code.</summary>
    public string Code { get; private set; }

    /// <summary>The facility name.</summary>
    public string Name { get; private set; }

    /// <summary>The hire charge per day.</summary>
    public decimal DailyRate { get; set; }

    /// <summary>The revenue account hire is credited to.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>Whether it can be booked.</summary>
    public bool IsActive { get; set; }
}

/// <summary>Where a facility booking stands.</summary>
public enum FacilityBookingStatus
{
    /// <summary>Booked and invoiced.</summary>
    Booked = 0,

    /// <summary>Cancelled; any refund credited.</summary>
    Cancelled = 1,
}

/// <summary>
/// A booking of a council facility for a range of days (<c>mun.facility_booking</c>): invoiced at the daily rate when
/// made; a facility cannot be booked twice for the same day.
/// </summary>
public sealed class FacilityBooking : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FacilityBooking()
    {
        BookingNumber = string.Empty;
        Purpose = string.Empty;
    }

    /// <summary>Makes a booking.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="bookingNumber">The booking number (also the invoice number).</param>
    /// <param name="facilityId">The facility.</param>
    /// <param name="customerId">The hirer.</param>
    /// <param name="fromDate">The first day.</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="fee">The hire fee.</param>
    /// <param name="purpose">What it is for.</param>
    public FacilityBooking(long companyId, string bookingNumber, long facilityId, long customerId, DateOnly fromDate, DateOnly toDate, decimal fee, string purpose)
    {
        CompanyId = companyId;
        BookingNumber = bookingNumber;
        FacilityId = facilityId;
        CustomerId = customerId;
        FromDate = fromDate;
        ToDate = toDate;
        Fee = fee;
        Purpose = purpose;
        Status = FacilityBookingStatus.Booked;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The booking number (also the invoice number).</summary>
    public string BookingNumber { get; private set; }

    /// <summary>The facility.</summary>
    public long FacilityId { get; private set; }

    /// <summary>The hirer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The first day.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>The last day.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>The hire fee.</summary>
    public decimal Fee { get; private set; }

    /// <summary>What it is for.</summary>
    public string Purpose { get; private set; }

    /// <summary>Where it stands.</summary>
    public FacilityBookingStatus Status { get; private set; }

    /// <summary>The refund credited on cancellation.</summary>
    public decimal? Refunded { get; private set; }

    /// <summary>Cancels it.</summary>
    /// <param name="refund">The refund credited.</param>
    public void Cancel(decimal refund)
    {
        Status = FacilityBookingStatus.Cancelled;
        Refunded = refund;
    }
}
