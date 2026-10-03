using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers <see cref="Dunning.ApplyCredits"/>: credits go against the oldest overdue invoices first.</summary>
public sealed class DunningCreditTests
{
    private static readonly DateOnly AsAt = new(2026, 4, 1);

    [Fact]
    public void Without_credits_the_oldest_invoice_sets_the_age()
    {
        NetOverdue net = Dunning.ApplyCredits([new OverdueItem(new DateOnly(2026, 1, 1), 300m), new OverdueItem(new DateOnly(2026, 3, 15), 500m)], 0m, AsAt);

        net.Should().Be(new NetOverdue(800m, 90, 0m));
    }

    [Fact]
    public void Credits_covering_the_oldest_invoice_lower_the_age()
    {
        NetOverdue net = Dunning.ApplyCredits([new OverdueItem(new DateOnly(2026, 3, 15), 500m), new OverdueItem(new DateOnly(2026, 1, 1), 300m)], 300m, AsAt);

        net.Should().Be(new NetOverdue(500m, 17, 300m));
    }

    [Fact]
    public void A_partly_covered_invoice_still_counts_its_age()
    {
        NetOverdue net = Dunning.ApplyCredits([new OverdueItem(new DateOnly(2026, 1, 1), 300m), new OverdueItem(new DateOnly(2026, 3, 15), 500m)], 100m, AsAt);

        net.Should().Be(new NetOverdue(700m, 90, 100m));
    }

    [Fact]
    public void Credits_covering_everything_leave_nothing_to_chase()
    {
        NetOverdue net = Dunning.ApplyCredits([new OverdueItem(new DateOnly(2026, 1, 1), 300m)], 1_000m, AsAt);

        net.Should().Be(new NetOverdue(0m, 0, 300m));
        Dunning.LevelFor(net.OldestDaysOverdue, DunningStages.Default).Should().Be(0);
    }
}
