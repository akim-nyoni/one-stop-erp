using FluentAssertions;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

/// <summary>Covers billing cycle periods, instalment slots, the share of a period in place and instalment amounts.</summary>
public sealed class BillingCyclesTests
{
    [Theory]
    [InlineData(BillingCycle.Quarterly, 1, "2026-05-10", "2026-04-01", "2026-06-30")]
    [InlineData(BillingCycle.Annual, 7, "2026-03-15", "2025-07-01", "2026-06-30")]
    [InlineData(BillingCycle.Annual, 7, "2026-07-01", "2026-07-01", "2027-06-30")]
    [InlineData(BillingCycle.HalfYearly, 7, "2026-12-31", "2026-07-01", "2026-12-31")]
    [InlineData(BillingCycle.Monthly, 1, "2026-02-14", "2026-02-01", "2026-02-28")]
    public void A_date_falls_in_its_cycle_period(BillingCycle cycle, int startMonth, string date, string start, string end)
    {
        CycleSlot slot = BillingCycles.Slot(cycle, startMonth, 1, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        (slot.PeriodStart, slot.PeriodEnd).Should().Be((DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
        slot.Instalment.Should().Be(1);
    }

    [Fact]
    public void Instalments_run_from_the_period_start_and_the_last_takes_the_rounding()
    {
        // An annual July charge of 1000 in 3 instalments: July, August, September; nothing from October.
        CycleSlot july = BillingCycles.Slot(BillingCycle.Annual, 7, 3, new DateOnly(2026, 7, 20));
        CycleSlot september = BillingCycles.Slot(BillingCycle.Annual, 7, 3, new DateOnly(2026, 9, 1));
        CycleSlot october = BillingCycles.Slot(BillingCycle.Annual, 7, 3, new DateOnly(2026, 10, 1));
        (july.Instalment, september.Instalment, october.Instalment).Should().Be((1, 3, 0));
        (BillingCycles.InstalmentAmount(1000m, july), BillingCycles.InstalmentAmount(1000m, september), BillingCycles.InstalmentAmount(1000m, october)).Should().Be((333.33m, 333.34m, 0m));
        july.BillMonth(new DateOnly(2026, 7, 20)).Should().Be(new DateOnly(2026, 7, 1));
        BillingCycles.Slot(BillingCycle.Annual, 7, 1, new DateOnly(2026, 9, 3)).BillMonth(new DateOnly(2026, 9, 3)).Should().Be(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void A_service_connected_part_way_pays_its_share_of_the_period()
    {
        CycleSlot q = BillingCycles.Slot(BillingCycle.Quarterly, 1, 1, new DateOnly(2026, 1, 10)); // 90 days
        BillingCycles.Share(q, new DateOnly(2026, 2, 15), null).Should().Be(45m / 90m);
        BillingCycles.Share(q, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)).Should().Be(0m);
    }
}
