using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class InstalmentPlanTests
{
    private static readonly DateOnly First = new(2026, 2, 1);

    [Fact]
    public void Reducing_balance_is_a_level_annuity_that_clears_the_principal()
    {
        // 12,000 at 12% over 12 months: 1,066.19 a month, the last 1,066.14; reference figures computed independently.
        IReadOnlyList<PlannedInstalment> plan = InstalmentPlan.Build(12000m, 12m, 12, InterestMethod.ReducingBalance, First);
        plan[0].Should().Be(new PlannedInstalment(1, First, 1066.19m, 120.00m, 946.19m, 11053.81m));
        plan[1].Should().Be(new PlannedInstalment(2, First.AddMonths(1), 1066.19m, 110.54m, 955.65m, 10098.16m));
        plan[11].Should().Be(new PlannedInstalment(12, First.AddMonths(11), 1066.14m, 10.56m, 1055.58m, 0m));
        plan.Sum(i => i.Interest).Should().Be(794.23m);
        plan.Sum(i => i.Principal).Should().Be(12000m);
    }

    [Fact]
    public void Flat_interest_is_charged_on_the_original_amount_and_spread_evenly()
    {
        // 10,000 at 10% flat for 24 months: 2,000 interest, 500 a month.
        IReadOnlyList<PlannedInstalment> plan = InstalmentPlan.Build(10000m, 10m, 24, InterestMethod.Flat, First);
        plan.Should().OnlyContain(i => i.Amount == 500m);
        plan[0].Interest.Should().Be(83.33m);
        plan[^1].Should().Be(new PlannedInstalment(24, First.AddMonths(23), 500m, 83.41m, 416.59m, 0m));
        plan.Sum(i => i.Interest).Should().Be(2000m);
    }

    [Fact]
    public void Zero_interest_spreads_the_principal_with_the_last_taking_the_rounding()
    {
        IReadOnlyList<PlannedInstalment> plan = InstalmentPlan.Build(1000m, 15m, 3, InterestMethod.Zero, First);
        plan.Select(i => i.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
        plan.Should().OnlyContain(i => i.Interest == 0m);
    }

    [Fact]
    public void Early_settlement_rebates_the_interest_of_instalments_not_yet_due()
    {
        IReadOnlyList<PlannedInstalment> plan = InstalmentPlan.Build(12000m, 12m, 12, InterestMethod.ReducingBalance, First);
        SettlementQuote quote = InstalmentPlan.Settle(plan, new DateOnly(2026, 3, 15)); // instalments 1 and 2 due
        quote.PrincipalOutstanding.Should().Be(10098.16m);
        quote.InterestRebated.Should().Be(794.23m - 120.00m - 110.54m);
        InstalmentPlan.Settle(plan, new DateOnly(2026, 1, 1)).PrincipalOutstanding.Should().Be(12000m);
    }

    [Fact]
    public void A_term_under_a_month_is_refused()
    {
        Action act = () => InstalmentPlan.Build(1000m, 10m, 0, InterestMethod.Flat, First);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
