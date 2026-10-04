using FluentAssertions;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class SlaClockTests
{
    private static readonly DateTimeOffset Reported = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TicketPriority.Critical, 3)]
    [InlineData(TicketPriority.High, 6)]
    [InlineData(TicketPriority.Normal, 12)]
    [InlineData(TicketPriority.Low, 24)]
    public void Urgency_shortens_the_target(TicketPriority priority, int hours)
    {
        SlaClock.Due(Reported, 12m, priority).Should().Be(Reported.AddHours(hours));
    }

    [Fact]
    public void Fractional_targets_round_to_the_minute()
    {
        SlaClock.Due(Reported, 1.5m, TicketPriority.Critical).Should().Be(Reported.AddMinutes(23)); // 22.5 minutes, rounded up
    }

    [Fact]
    public void Done_targets_are_met_or_breached()
    {
        DateTimeOffset due = Reported.AddHours(3);
        SlaClock.State(Reported, due, due, due.AddDays(9)).Should().Be(SlaState.Met);
        SlaClock.State(Reported, due, due.AddMinutes(1), due).Should().Be(SlaState.Breached);
    }

    [Fact]
    public void Open_targets_are_on_track_at_risk_from_80_percent_then_breached()
    {
        DateTimeOffset due = Reported.AddHours(10);
        SlaClock.State(Reported, due, null, Reported.AddHours(7.9)).Should().Be(SlaState.OnTrack);
        SlaClock.State(Reported, due, null, Reported.AddHours(8)).Should().Be(SlaState.AtRisk);
        SlaClock.State(Reported, due, null, due).Should().Be(SlaState.AtRisk);
        SlaClock.State(Reported, due, null, due.AddSeconds(1)).Should().Be(SlaState.Breached);
    }
}
