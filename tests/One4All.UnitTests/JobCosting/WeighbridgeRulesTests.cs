using FluentAssertions;
using One4All.Application.JobCosting;
using One4All.Core.JobCosting;
using Xunit;

namespace One4All.UnitTests.JobCosting;

public sealed class WeighbridgeRulesTests
{
    private static readonly HaulageBand[] Bands = [new("Z1", 0m, 10m, 2m, 30m), new("Z2", 10m, 50m, 4m, 50m)];

    [Fact]
    public void Haulage_uses_the_band_and_its_minimum()
    {
        WeighbridgeRules.Haulage(Bands, 5m, 5m).Should().Be(("Z1", 30m));
        WeighbridgeRules.Haulage(Bands, 10m, 22m).Should().Be(("Z2", 88m)); // 10 km is the start of Z2
        WeighbridgeRules.Haulage(Bands, 60m, 22m).Should().BeNull();
    }

    [Fact]
    public void Manual_weights_need_a_reason_and_a_second_person()
    {
        WeighbridgeRules.ValidateWeight(0m, false, null, 1, null)!.Code.Should().Be("Wb.Weight");
        WeighbridgeRules.ValidateWeight(100m, false, null, 1, null).Should().BeNull();
        WeighbridgeRules.ValidateWeight(100m, true, null, 1, 2)!.Code.Should().Be("Wb.ManualReason");
        WeighbridgeRules.ValidateWeight(100m, true, "offline", 1, 1)!.Code.Should().Be("Wb.ManualAuthorisation");
        WeighbridgeRules.ValidateWeight(100m, true, "offline", 1, 2).Should().BeNull();
    }

    [Fact]
    public void Turnaround_is_whole_minutes()
    {
        DateTimeOffset start = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        WeighbridgeRules.TurnaroundMinutes(start, start.AddMinutes(42).AddSeconds(40)).Should().Be(43);
        WeighbridgeRules.TurnaroundMinutes(start, null).Should().BeNull();
    }

    [Fact]
    public void Outbound_tickets_weigh_tare_first()
    {
        WeighbridgeTicket ticket = WeighbridgeTicket.Outbound(1, "wb-1", "abc", "Sand", 10_000m, 0.6m, new DateOnly(2026, 3, 1));
        ticket.Direction.Should().Be(WeighDirection.Outbound);
        ticket.Complete(32_000m, DateTimeOffset.UnixEpoch);
        (ticket.GrossWeightKg, ticket.TareWeightKg, ticket.NetTonnes, ticket.NetCubicMetres).Should().Be((32_000m, 10_000m, 22m, 13.2m));
    }
}
