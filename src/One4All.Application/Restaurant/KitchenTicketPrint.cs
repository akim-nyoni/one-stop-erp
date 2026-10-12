using System.Globalization;
using System.Text;
using One4All.Application.Pos;

namespace One4All.Application.Restaurant;

/// <summary>A kitchen order ticket as printed at its station.</summary>
/// <param name="Station">The station (KITCHEN for the main kitchen).</param>
/// <param name="TicketNumber">The ticket number.</param>
/// <param name="OrderNumber">The order.</param>
/// <param name="Where">The table, or the order type (takeaway, delivery …) and customer.</param>
/// <param name="Course">The course fired, if one.</param>
/// <param name="SentAt">When it was fired.</param>
/// <param name="Lines">The dishes: quantity and description.</param>
/// <param name="Reprint">Whether this is a reprint.</param>
public sealed record KitchenTicketDocument(string Station, string TicketNumber, string OrderNumber, string Where, string? Course, DateTimeOffset SentAt,
    IReadOnlyList<(decimal Quantity, string Description)> Lines, bool Reprint);

/// <summary>
/// Prints a kitchen order ticket for its station: the station and ticket in large type, where it goes, the course, the time, and each
/// dish with its quantity first, so the cook reads it at a glance (brief §4.2). A reprint says so. Pure and unit-tested.
/// </summary>
public static class KitchenTicketFormatter
{
    /// <summary>The ticket as fixed-width text.</summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="width">The paper width in characters.</param>
    /// <returns>The text.</returns>
    public static string ToPlainText(KitchenTicketDocument ticket, int width = 42)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        StringBuilder sb = new();
        foreach (string line in Lines(ticket, width))
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    /// <summary>The ticket as ESC/POS bytes for a station printer: header in double size, the dishes in bold, then a cut.</summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="width">The paper width in characters.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ToEscPos(KitchenTicketDocument ticket, int width = 42)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        EscPosBuilder p = new();
        p.Align(1).DoubleSize(true).Line(ticket.Station).Line(ticket.TicketNumber).DoubleSize(false);
        if (ticket.Reprint)
        {
            p.Bold(true).Line("*** REPRINT ***").Bold(false);
        }

        p.Align(0).Line(ticket.Where).Line(Header(ticket)).Line(new string('-', width)).Bold(true);
        foreach ((decimal quantity, string description) in ticket.Lines)
        {
            p.Line(Dish(quantity, description, width));
        }

        return p.Bold(false).Line(new string('-', width)).Feed(3).Cut().ToArray();
    }

    private static IEnumerable<string> Lines(KitchenTicketDocument ticket, int width)
    {
        yield return Centre(ticket.Station, width);
        yield return Centre(ticket.TicketNumber, width);
        if (ticket.Reprint)
        {
            yield return Centre("*** REPRINT ***", width);
        }

        yield return ticket.Where;
        yield return Header(ticket);
        yield return new string('-', width);
        foreach ((decimal quantity, string description) in ticket.Lines)
        {
            yield return Dish(quantity, description, width);
        }

        yield return new string('-', width);
    }

    private static string Header(KitchenTicketDocument ticket)
        => $"Order {ticket.OrderNumber}  {ticket.SentAt.ToString("HH:mm", CultureInfo.InvariantCulture)}" + (ticket.Course is string course ? $"  {course.ToUpperInvariant()}" : string.Empty);

    private static string Dish(decimal quantity, string description, int width)
    {
        string line = $"{quantity.ToString("0.##", CultureInfo.InvariantCulture),3} x {description}";
        return line.Length > width ? line[..width] : line;
    }

    private static string Centre(string text, int width)
        => text.Length >= width ? text[..width] : new string(' ', (width - text.Length) / 2) + text;
}
