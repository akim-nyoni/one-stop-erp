using FluentAssertions;
using One4All.Core.JobCosting;
using Xunit;

namespace One4All.UnitTests.JobCosting;

public class WeighbridgeTicketTests
{
    private static WeighbridgeTicket Ticket() =>
        new(1, "WB-1", "ND 123-456", "River Sand", grossWeightKg: 32000m, cubicMetresPerTonne: 0.65m, new DateOnly(2026, 4, 1));

    [Fact]
    public void Net_is_zero_until_second_weigh()
    {
        Ticket().NetWeightKg.Should().Be(0m);
    }

    [Fact]
    public void Net_tonnes_and_cubic_metres_after_tare()
    {
        WeighbridgeTicket t = Ticket();
        t.SecondWeigh(12000m); // net 20,000 kg = 20 t
        t.NetWeightKg.Should().Be(20000m);
        t.NetTonnes.Should().Be(20m);
        t.NetCubicMetres.Should().Be(13m); // 20 t × 0.65 m³/t
    }

    [Fact]
    public void Moisture_deduction_reduces_net()
    {
        WeighbridgeTicket t = Ticket();
        t.SecondWeigh(12000m);
        t.DeductionKg = 500m;
        t.NetWeightKg.Should().Be(19500m);
        t.NetTonnes.Should().Be(19.5m);
    }

    [Fact]
    public void Net_never_goes_negative()
    {
        WeighbridgeTicket t = Ticket();
        t.SecondWeigh(33000m); // tare above gross (re-weigh error)
        t.NetWeightKg.Should().Be(0m);
    }
}
