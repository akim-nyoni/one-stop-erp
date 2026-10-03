using FluentAssertions;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class MeterReadingRulesTests
{
    private static readonly ReadingThresholds Thresholds = ReadingThresholds.Default;

    private static MeterState State(decimal last, decimal estimated = 0m, decimal credit = 0m, int digits = 4, decimal multiplier = 1m)
        => new(last, estimated, credit, digits, multiplier);

    [Fact]
    public void A_normal_reading_bills_the_advance_times_the_multiplier()
    {
        MeterReadingRules.Assess(State(100m, multiplier: 10m), 103m, 30m, Thresholds, false)
            .Should().Be(new ReadingAssessment(30m, 30m, 0m, ReadingFlags.None, true));
    }

    [Theory]
    [InlineData(161, ReadingFlags.HighConsumption)]
    [InlineData(114, ReadingFlags.LowConsumption)]
    [InlineData(130, ReadingFlags.None)]
    public void Consumption_far_from_the_expected_is_flagged(int reading, ReadingFlags flags)
    {
        MeterReadingRules.Assess(State(100m), reading, 30m, Thresholds, false).Flags.Should().Be(flags);
    }

    [Fact]
    public void Without_history_nothing_is_flagged()
    {
        MeterReadingRules.Assess(State(100m), 5000m, 0m, Thresholds, false).Flags.Should().Be(ReadingFlags.None);
    }

    [Fact]
    public void A_wrap_past_the_last_dial_is_a_rollover()
    {
        ReadingAssessment a = MeterReadingRules.Assess(State(9980m), 20m, 0m, Thresholds, false);
        a.Flags.Should().Be(ReadingFlags.Rollover);
        a.BilledConsumption.Should().Be(40m);
        a.Accepted.Should().BeTrue();
    }

    [Fact]
    public void A_lower_reading_without_a_plausible_wrap_is_rejected()
    {
        MeterReadingRules.Assess(State(9130m), 9100m, 0m, Thresholds, false)
            .Should().Be(new ReadingAssessment(0m, 0m, 0m, ReadingFlags.Negative, false));
        MeterReadingRules.Assess(State(9130m, digits: 0), 20m, 0m, Thresholds, false).Flags.Should().Be(ReadingFlags.Negative);
    }

    [Fact]
    public void Suspected_tampering_is_rejected_and_bills_nothing()
    {
        ReadingAssessment a = MeterReadingRules.Assess(State(100m, credit: 5m), 130m, 30m, Thresholds, true);
        a.Should().Be(new ReadingAssessment(30m, 0m, 5m, ReadingFlags.Tamper, false));
    }

    [Fact]
    public void An_actual_reading_bills_only_what_was_not_estimated()
    {
        MeterReadingRules.Assess(State(100m, estimated: 50m), 170m, 0m, Thresholds, false)
            .Should().Be(new ReadingAssessment(70m, 20m, 0m, ReadingFlags.None, true));
    }

    [Fact]
    public void An_over_estimate_becomes_a_credit_netted_off_later()
    {
        ReadingAssessment first = MeterReadingRules.Assess(State(100m, estimated: 50m), 130m, 0m, Thresholds, false);
        first.Should().Be(new ReadingAssessment(30m, 0m, 20m, ReadingFlags.EstimateCorrection, true));
        MeterReadingRules.Assess(State(130m, credit: first.UnitsCreditAfter), 180m, 0m, Thresholds, false).BilledConsumption.Should().Be(30m);
    }

    [Fact]
    public void Expected_consumption_follows_the_historical_daily_rate()
    {
        ConsumptionPoint[] history = [new(new DateOnly(2026, 1, 1), 0m), new(new DateOnly(2026, 1, 31), 30m), new(new DateOnly(2026, 3, 2), 70m)];
        MeterReadingRules.Expected(history, 30).Should().Be(50m);
        MeterReadingRules.Expected(history[..1], 30).Should().Be(0m);
        MeterReadingRules.Expected(history, 0).Should().Be(0m);
    }

    [Theory]
    [InlineData(9150, 50, 4, 1, 9200)]
    [InlineData(9980, 50, 4, 1, 30)]
    [InlineData(100, 50, 0, 10, 105)]
    public void Estimated_readings_advance_and_wrap_the_register(int last, int units, int digits, int multiplier, int expected)
    {
        MeterReadingRules.EstimatedReading(last, units, digits, multiplier).Should().Be(expected);
    }
}
