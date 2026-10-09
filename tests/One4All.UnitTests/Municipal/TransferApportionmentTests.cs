using FluentAssertions;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

/// <summary>Covers the pro-rata settlement of a period's charges between the seller and buyer of a property.</summary>
public sealed class TransferApportionmentTests
{
    [Fact]
    public void A_monthly_charge_billed_to_the_seller_is_shared_by_the_days_each_owns_the_property()
    {
        DateOnly transfer = new(2026, 4, 11);
        CycleSlot month = BillingCycles.Slot(BillingCycle.Monthly, 1, 1, transfer);
        TransferApportionment.SellerShare(300m, month, transfer).Should().Be(100m, "10 of April's 30 days");
        TransferApportionment.Settlement(300m, month, transfer).Should().Be(200m, "the buyer's 20 days are credited to the seller and charged to the buyer");
    }

    [Fact]
    public void An_annual_charge_in_advance_or_in_instalments_settles_what_was_billed_against_the_sellers_days()
    {
        DateOnly transfer = new(2026, 4, 1);
        CycleSlot year = BillingCycles.Slot(BillingCycle.Annual, 1, 1, transfer);
        TransferApportionment.BilledSoFar(3650m, year).Should().Be(3650m);
        TransferApportionment.Settlement(3650m, year, transfer).Should().Be(3650m - 900m, "Jan–Mar is 90 of 365 days");

        CycleSlot instalments = BillingCycles.Slot(BillingCycle.Annual, 1, 12, transfer);
        TransferApportionment.BilledSoFar(3650m, instalments).Should().Be(4 * 304.17m, "April's instalment is the fourth");
        TransferApportionment.Settlement(3650m, instalments, transfer).Should().Be(4 * 304.17m - 900m);

        CycleSlot afterInstalments = BillingCycles.Slot(BillingCycle.Annual, 1, 2, new DateOnly(2026, 12, 1));
        TransferApportionment.BilledSoFar(3650m, afterInstalments).Should().Be(3650m, "both instalments fell before December");
        TransferApportionment.Settlement(3650m, afterInstalments, new DateOnly(2026, 12, 1)).Should().Be(3650m - 3340m, "the buyer owns December's 31 days");
    }
}
