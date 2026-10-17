using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The lifecycle of a waitlist entry (Restaurant module).</summary>
public enum WaitlistStatus
{
    /// <summary>Waiting - a walk-in party queued for a table.</summary>
    Waiting = 1,

    /// <summary>Seated - the party was seated at a table.</summary>
    Seated = 2,

    /// <summary>Left - the party gave up and left before being seated.</summary>
    Left = 3,

    /// <summary>No-show - the party was called but did not return.</summary>
    NoShow = 4,
}

/// <summary>
/// A walk-in waitlist entry (<c>res.waitlist</c>): a party queued for a table when none is free, with a
/// quoted wait. Operational only (no GL). Seating it opens a table order and links it.
/// </summary>
public sealed class WaitlistEntry : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private WaitlistEntry() => GuestName = string.Empty;

    /// <summary>Adds a party to the waitlist.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="guestName">The guest's name.</param>
    /// <param name="partySize">The number of covers.</param>
    /// <param name="quotedMinutes">The quoted wait, in minutes.</param>
    /// <param name="addedOn">When the party joined the queue.</param>
    /// <param name="guestPhone">The guest's contact number, if any.</param>
    /// <param name="notes">Any notes (seating preference), if any.</param>
    public WaitlistEntry(long companyId, string guestName, int partySize, int quotedMinutes, DateTime addedOn, string? guestPhone = null, string? notes = null)
    {
        CompanyId = companyId;
        GuestName = guestName.Trim();
        PartySize = partySize;
        QuotedMinutes = quotedMinutes;
        AddedOn = addedOn;
        GuestPhone = guestPhone;
        Notes = notes;
        Status = WaitlistStatus.Waiting;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The guest's name.</summary>
    public string GuestName { get; private set; }

    /// <summary>The guest's contact number, if any.</summary>
    public string? GuestPhone { get; private set; }

    /// <summary>The number of covers.</summary>
    public int PartySize { get; private set; }

    /// <summary>The quoted wait, in minutes.</summary>
    public int QuotedMinutes { get; private set; }

    /// <summary>When the party joined the queue.</summary>
    public DateTime AddedOn { get; private set; }

    /// <summary>Any notes (seating preference).</summary>
    public string? Notes { get; set; }

    /// <summary>The status.</summary>
    public WaitlistStatus Status { get; private set; }

    /// <summary>The table the party was seated at, if any.</summary>
    public long? SeatedTableId { get; private set; }

    /// <summary>The order opened when the party was seated, if any.</summary>
    public long? SeatedOrderId { get; private set; }

    /// <summary>Seats the party, linking the table and opened order.</summary>
    /// <param name="tableId">The table the party was seated at.</param>
    /// <param name="orderId">The order opened for the party.</param>
    public void Seat(long tableId, long orderId)
    {
        Status = WaitlistStatus.Seated;
        SeatedTableId = tableId;
        SeatedOrderId = orderId;
    }

    /// <summary>Marks the party as having left the queue.</summary>
    public void Leave() => Status = WaitlistStatus.Left;

    /// <summary>Marks the party a no-show.</summary>
    public void MarkNoShow() => Status = WaitlistStatus.NoShow;
}
