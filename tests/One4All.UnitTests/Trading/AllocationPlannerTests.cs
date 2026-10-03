using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the allocation planner: exact pairs first, then oldest due first, never over-allocating.</summary>
public sealed class AllocationPlannerTests
{
    private static AllocatableItem Item(long id, int month, decimal amount) =>
        new(id, new DateOnly(2026, month, 1), new DateOnly(2026, month, 1).AddDays(30), amount);

    [Fact]
    public void Exact_amount_wins_over_oldest_then_the_rest_goes_oldest_first()
    {
        AllocatableItem[] invoices = [Item(1, 1, 100m), Item(2, 2, 250m), Item(3, 3, 400m)];
        AllocatableItem[] receipts = [Item(10, 3, 250m), Item(11, 4, 300m)];

        IReadOnlyList<PlannedAllocation> plan = AllocationPlanner.Plan(invoices, receipts);

        plan.Should().HaveCount(3);
        plan[0].Should().Be(new PlannedAllocation(2, 10, 250m, true)); // the 250 receipt settles the 250 invoice, not the January one
        plan[1].Should().Be(new PlannedAllocation(1, 11, 100m, false)); // then oldest first: January in full
        plan[2].Should().Be(new PlannedAllocation(3, 11, 200m, false)); // and 200 of March
    }

    [Fact]
    public void Never_allocates_more_than_either_side_holds()
    {
        AllocatableItem[] invoices = [Item(1, 1, 80m)];
        AllocatableItem[] receipts = [Item(10, 1, 50m), Item(11, 2, 50m)];

        IReadOnlyList<PlannedAllocation> plan = AllocationPlanner.Plan(invoices, receipts);

        plan.Sum(p => p.Amount).Should().Be(80m);
        plan.Where(p => p.CreditItemId == 11).Sum(p => p.Amount).Should().Be(30m);
    }

    [Fact]
    public void Nothing_to_do_when_a_side_is_empty_or_zero()
    {
        AllocationPlanner.Plan([Item(1, 1, 50m)], []).Should().BeEmpty();
        AllocationPlanner.Plan([Item(1, 1, 0m)], [Item(2, 1, 50m)]).Should().BeEmpty();
    }

    [Fact]
    public void Symmetric_for_payables_where_invoices_are_credits()
    {
        // AP: payments are debits, invoices credits. A 500 payment against invoices of 300 (old) and 500 (new).
        AllocatableItem[] payments = [Item(20, 5, 500m)];
        AllocatableItem[] invoices = [Item(30, 1, 300m), Item(31, 2, 500m)];

        AllocationPlanner.Plan(payments, invoices).Should().ContainSingle()
            .Which.Should().Be(new PlannedAllocation(20, 31, 500m, true));
    }
}
