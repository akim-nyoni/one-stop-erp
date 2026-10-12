using FluentAssertions;
using One4All.Application.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers reading cashier card swipes and hashing them.</summary>
public sealed class CashierCardsTests
{
    [Theory]
    [InlineData("%B6011000990139424^CASHIER/ANN^2912?", "6011000990139424")]
    [InlineData(";6011000990139424=2912?", "6011000990139424")]
    [InlineData(" 6011 0009 9013 9424 ", "6011000990139424")]
    [InlineData("emp-0042", "EMP0042")]
    public void Swipes_and_scans_read_the_card_number(string read, string number) => CashierCards.Normalise(read).Should().Be(number);

    [Fact]
    public void The_same_card_hashes_the_same_however_it_was_read()
    {
        CashierCards.Hash(";6011000990139424=2912?").Should().Be(CashierCards.Hash("6011 0009 9013 9424")).And.HaveLength(64);
        CashierCards.Hash("12").Should().BeEmpty();
    }
}
