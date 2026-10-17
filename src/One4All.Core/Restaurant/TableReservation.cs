using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The lifecycle of a table reservation (Restaurant module).</summary>
public enum ReservationStatus
{
    /// <summary>Booked - a future reservation awaiting the guest.</summary>
    Booked = 1,

    /// <summary>Seated - the guest arrived and an order was opened.</summary>
    Seated = 2,

    /// <summary>Cancelled by the guest or the house.</summary>
    Cancelled = 3,

    /// <summary>No-show - the guest never arrived.</summary>
    NoShow = 4,
}

/// <summary>
/// A table reservation (<c>res.reservation</c>): a guest booking a table for a party at a future time.
/// Operational only (no GL). When the guest arrives the reservation is <see cref="Seat"/>ed, which opens
/// the table order; otherwise it is cancelled or marked a no-show.
/// </summary>
public sealed class TableReservation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TableReservation() => GuestName = string.Empty;

    /// <summary>Creates a reservation.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableId">The reserved table.</param>
    /// <param name="guestName">The guest's name.</param>
    /// <param name="partySize">The number of covers.</param>
    /// <param name="reservedFor">The date and time the table is booked for.</param>
    /// <param name="guestPhone">The guest's contact number, if any.</param>
    /// <param name="notes">Any notes (occasion, seating preference), if any.</param>
    public TableReservation(long companyId, long tableId, string guestName, int partySize, DateTime reservedFor, string? guestPhone = null, string? notes = null)
    {
        CompanyId = companyId;
        TableId = tableId;
        GuestName = guestName.Trim();
        PartySize = partySize;
        ReservedFor = reservedFor;
        GuestPhone = guestPhone;
        Notes = notes;
        Status = ReservationStatus.Booked;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The reserved table.</summary>
    public long TableId { get; private set; }

    /// <summary>The guest's name.</summary>
    public string GuestName { get; private set; }

    /// <summary>The guest's contact number, if any.</summary>
    public string? GuestPhone { get; private set; }

    /// <summary>The number of covers.</summary>
    public int PartySize { get; private set; }

    /// <summary>The date and time the table is booked for.</summary>
    public DateTime ReservedFor { get; private set; }

    /// <summary>Any notes (occasion, seating preference).</summary>
    public string? Notes { get; set; }

    /// <summary>The status.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>The order opened when the guest was seated, if any.</summary>
    public long? SeatedOrderId { get; private set; }

    /// <summary>The cancellation reason, if cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Seats the guest, linking the opened table order.</summary>
    /// <param name="orderId">The order opened for the guest.</param>
    public void Seat(long orderId)
    {
        Status = ReservationStatus.Seated;
        SeatedOrderId = orderId;
    }

    /// <summary>Cancels the reservation with a reason.</summary>
    /// <param name="reason">The cancellation reason.</param>
    public void Cancel(string reason)
    {
        Status = ReservationStatus.Cancelled;
        CancellationReason = reason;
    }

    /// <summary>Marks the reservation a no-show.</summary>
    public void MarkNoShow() => Status = ReservationStatus.NoShow;
}
