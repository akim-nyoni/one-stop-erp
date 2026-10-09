using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers the cash budget: lines turn into cash by their timing and the months run from the opening cash.</summary>
public sealed class CashBudgetTests
{
    [Fact]
    public void Sales_are_collected_over_the_following_months_and_purchases_paid_on_terms()
    {
        CashTiming[] timings = [new("4", [50m, 30m, 20m]), new("5", [0m, 100m]), new("51", [100m])];
        CashBudget.Problem(timings[0]).Should().BeNull();
        CashBudget.Problem(new CashTiming("4", [50m, 30m])).Should().NotBeNull();

        (string, int, decimal)[] lines = [("4000", 1, -1_000m), ("5000", 1, 400m), ("5100", 1, 100m), ("4000", 12, -1_000m)];
        IReadOnlyList<CashBudgetRow> rows = CashBudget.Project(lines, timings, 200m, out decimal carryOut);
        rows.Should().HaveCount(12);
        (rows[0].Opening, rows[0].Receipts, rows[0].Payments, rows[0].Closing).Should().Be((200m, 500m, 100m, 600m), "half the sales, the rent paid at once");
        (rows[1].Receipts, rows[1].Payments).Should().Be((300m, 400m), "purchases paid the month after");
        rows[2].Receipts.Should().Be(200m);
        rows[11].Receipts.Should().Be(500m);
        carryOut.Should().Be(500m, "December's sales still owed at the year end");
        rows[11].Closing.Should().Be(200m + 1_000m - 500m + 500m);
    }
}
