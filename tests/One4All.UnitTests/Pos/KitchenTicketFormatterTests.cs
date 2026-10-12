using FluentAssertions;
using One4All.Application.Restaurant;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers the kitchen order ticket as printed at its station.</summary>
public sealed class KitchenTicketFormatterTests
{
    private static KitchenTicketDocument Ticket(bool reprint) => new("GRILL", "K-17-GRILL", "ORD-9", "Table 4", "Main", new DateTimeOffset(2026, 3, 1, 19, 5, 0, TimeSpan.Zero),
        [(2m, "Rump steak 300g"), (1m, "Chicken half")], reprint);

    [Fact]
    public void Ticket_shows_station_table_course_time_and_dishes()
    {
        string text = KitchenTicketFormatter.ToPlainText(Ticket(false), 32);
        text.Should().Contain("GRILL").And.Contain("K-17-GRILL").And.Contain("Table 4").And.Contain("Order ORD-9  19:05  MAIN")
            .And.Contain("  2 x Rump steak 300g").And.Contain("  1 x Chicken half").And.NotContain("REPRINT");
        KitchenTicketFormatter.ToPlainText(Ticket(true), 32).Should().Contain("*** REPRINT ***");
    }

    [Fact]
    public void EscPos_ends_with_a_cut()
    {
        byte[] bytes = KitchenTicketFormatter.ToEscPos(Ticket(false));
        bytes[..2].Should().Equal(0x1B, (byte)'@');
        bytes[^4..].Should().Equal(0x1D, (byte)'V', 66, 3);
    }
}
