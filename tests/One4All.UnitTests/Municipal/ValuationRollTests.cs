using FluentAssertions;
using One4All.Application.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class ValuationRollTests
{
    private static readonly DatedValue Opening = new(DateOnly.MinValue, 100_000m);

    [Fact]
    public void The_latest_value_effective_by_the_date_applies()
    {
        DatedValue[] history = [Opening, new(new DateOnly(2026, 2, 1), 160_000m), new(new DateOnly(2027, 1, 1), 140_000m)];
        ValuationRoll.ValueAt(history, new DateOnly(2026, 1, 31), 0m).Should().Be(100_000m);
        ValuationRoll.ValueAt(history, new DateOnly(2026, 2, 1), 0m).Should().Be(160_000m);
        ValuationRoll.ValueAt(history, new DateOnly(2027, 6, 1), 0m).Should().Be(140_000m);
    }

    [Fact]
    public void A_later_record_with_the_same_date_replaces_the_earlier()
    {
        DatedValue[] history = [Opening, new(new DateOnly(2026, 2, 1), 160_000m), new(new DateOnly(2026, 2, 1), 130_000m)];
        ValuationRoll.ValueAt(history, new DateOnly(2026, 3, 1), 0m).Should().Be(130_000m);
    }

    [Fact]
    public void Records_made_out_of_date_order_still_apply_by_date()
    {
        DatedValue[] history = [Opening, new(new DateOnly(2027, 1, 1), 140_000m), new(new DateOnly(2026, 2, 1), 160_000m)];
        ValuationRoll.ValueAt(history, new DateOnly(2027, 2, 1), 0m).Should().Be(140_000m);
        ValuationRoll.ValueAt(history, new DateOnly(2026, 6, 1), 0m).Should().Be(160_000m);
    }

    [Fact]
    public void With_no_record_in_force_the_fallback_applies()
    {
        ValuationRoll.ValueAt([new DatedValue(new DateOnly(2026, 2, 1), 160_000m)], new DateOnly(2026, 1, 1), 99m).Should().Be(99m);
    }

    [Fact]
    public void Back_billing_charges_the_difference_on_each_bill_reached()
    {
        DatedValue[] before = [Opening];
        DatedValue[] after = [Opening, new(new DateOnly(2026, 2, 1), 160_000m)];
        DateOnly[] bills = [new(2026, 2, 15), new(2026, 3, 15)];
        ValuationRoll.BackBilling(bills, before, after, 0m, v => v * 0.001m).Should().Be(120m);
        ValuationRoll.BackBilling(bills, after, before, 0m, v => v * 0.001m).Should().Be(-120m);
    }

    [Fact]
    public void Back_billing_can_use_the_tariff_in_force_on_each_bill_date()
    {
        DatedValue[] before = [Opening];
        DatedValue[] after = [Opening, new(new DateOnly(2026, 1, 1), 200_000m)];
        DateOnly[] bills = [new(2026, 6, 15), new(2026, 7, 15)];
        decimal Rate(DateOnly d) => d >= new DateOnly(2026, 7, 1) ? 0.002m : 0.001m;
        ValuationRoll.BackBilling(bills, before, after, 0m, (d, v) => v * Rate(d)).Should().Be(300m);
    }

    [Fact]
    public void Back_billing_respects_the_charge_floor()
    {
        DatedValue[] before = [new(DateOnly.MinValue, 10_000m)];
        DatedValue[] after = [.. before, new(new DateOnly(2026, 1, 1), 20_000m)];
        ValuationRoll.BackBilling([new DateOnly(2026, 1, 15)], before, after, 0m, v => Math.Max(25m, v * 0.001m)).Should().Be(0m);
    }
}
