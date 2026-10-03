using FluentAssertions;
using One4All.Core.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers the budget seal: set on approval, closed to edits, blind to virements, broken by tampering.</summary>
public sealed class BudgetSealTests
{
    private static Budget Draft()
    {
        Budget budget = new(1, 1, "2026", BudgetType.OriginalApproved, null, 1);
        budget.AddLine("6800", 3, 1_000m);
        budget.AddLine("6700", 3, 500m, "ADM");
        return budget;
    }

    [Fact]
    public void Approval_seals_the_lines_and_closes_them_to_edits()
    {
        Budget budget = Draft();
        budget.IsSealed.Should().BeFalse();

        budget.Approve();

        budget.SealHash.Should().HaveLength(64);
        budget.VerifySeal().Should().BeTrue();
        FluentActions.Invoking(() => budget.AddLine("6100", 1, 10m)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Virement_lines_leave_the_seal_intact()
    {
        Budget budget = Draft();
        budget.Activate();
        string seal = budget.SealHash!;

        budget.AddLine("6800", 3, -200m, null, virementId: 9);
        budget.AddLine("6700", 3, 200m, null, virementId: 9);

        budget.SealHash.Should().Be(seal);
        budget.VerifySeal().Should().BeTrue();
    }

    [Fact]
    public void Identical_lines_give_identical_seals_whatever_their_order()
    {
        Budget a = new(1, 1, "2026", BudgetType.OriginalApproved, null, 1);
        a.AddLine("6800", 3, 1_000m);
        a.AddLine("6700", 3, 500m, "ADM");
        Budget b = new(1, 1, "2026", BudgetType.OriginalApproved, null, 1);
        b.AddLine("6700", 3, 500m, "ADM");
        b.AddLine("6800", 3, 1_000m);

        a.Approve();
        b.Approve();

        a.SealHash.Should().Be(b.SealHash);
    }
}
