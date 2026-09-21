using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The preparation state of a kitchen order ticket (Restaurant module).</summary>
public enum KitchenTicketStatus
{
    /// <summary>Sent to the kitchen.</summary>
    Sent = 1,

    /// <summary>Being prepared.</summary>
    Preparing = 2,

    /// <summary>Ready to serve.</summary>
    Ready = 3,

    /// <summary>Served to the table.</summary>
    Served = 4,
}

/// <summary>
/// A kitchen order ticket (KOT, <c>res.kitchen_ticket</c>): the items fired from a table order to the
/// kitchen for preparation. Operational only — no GL effect. Advances Sent → Preparing → Ready → Served.
/// </summary>
public sealed class KitchenTicket : AuditableEntity, ICompanyScoped
{
    private readonly List<KitchenTicketLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private KitchenTicket() => TicketNumber = string.Empty;

    /// <summary>Creates a ticket.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableOrderId">The table order the items came from.</param>
    /// <param name="ticketNumber">The ticket number.</param>
    /// <param name="sentOn">The date the ticket was fired.</param>
    /// <param name="course">The course this ticket fires, if it is a single-course ticket.</param>
    public KitchenTicket(long companyId, long tableOrderId, string ticketNumber, DateOnly sentOn, MenuCourse? course = null)
    {
        CompanyId = companyId;
        TableOrderId = tableOrderId;
        TicketNumber = ticketNumber;
        SentOn = sentOn;
        Course = course;
        Status = KitchenTicketStatus.Sent;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The table order the items came from.</summary>
    public long TableOrderId { get; private set; }

    /// <summary>The ticket number.</summary>
    public string TicketNumber { get; private set; }

    /// <summary>The date the ticket was fired.</summary>
    public DateOnly SentOn { get; private set; }

    /// <summary>The course this ticket fires, if it is a single-course ticket.</summary>
    public MenuCourse? Course { get; private set; }

    /// <summary>The status.</summary>
    public KitchenTicketStatus Status { get; private set; }

    /// <summary>The ticket lines.</summary>
    public IReadOnlyList<KitchenTicketLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a line to the ticket.</summary>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The created line.</returns>
    public KitchenTicketLine AddLine(long menuItemId, string description, decimal quantity)
    {
        KitchenTicketLine line = new(menuItemId, description, quantity);
        _lines.Add(line);
        return line;
    }

    /// <summary>Advances the ticket to the next preparation state (never backwards).</summary>
    /// <param name="status">The new status.</param>
    /// <returns>True when the transition was applied; false when it would move backwards.</returns>
    public bool Advance(KitchenTicketStatus status)
    {
        if ((int)status <= (int)Status)
        {
            return false;
        }

        Status = status;
        return true;
    }
}

/// <summary>One item on a kitchen order ticket (<c>res.kitchen_ticket_line</c>).</summary>
public sealed class KitchenTicketLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private KitchenTicketLine() => Description = string.Empty;

    /// <summary>Creates a line.</summary>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    public KitchenTicketLine(long menuItemId, string description, decimal quantity)
    {
        MenuItemId = menuItemId;
        Description = description;
        Quantity = quantity;
    }

    /// <summary>The owning ticket id.</summary>
    public long KitchenTicketId { get; private set; }

    /// <summary>The menu item.</summary>
    public long MenuItemId { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }
}
