using FluentAssertions;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

/// <summary>Covers IAS 29 arithmetic: which accounts are monetary, restatement factors and restated balances.</summary>
public sealed class HyperinflationTests
{
    [Theory]
    [InlineData(AccountType.Asset, ControlAccountType.None, Ias29Classification.Default, true)]
    [InlineData(AccountType.Liability, ControlAccountType.AccountsPayable, Ias29Classification.Default, true)]
    [InlineData(AccountType.Asset, ControlAccountType.Stock, Ias29Classification.Default, false)]
    [InlineData(AccountType.Equity, ControlAccountType.None, Ias29Classification.Default, false)]
    [InlineData(AccountType.Income, ControlAccountType.None, Ias29Classification.Default, false)]
    [InlineData(AccountType.Asset, ControlAccountType.None, Ias29Classification.NonMonetary, false)]
    [InlineData(AccountType.Equity, ControlAccountType.None, Ias29Classification.Monetary, true)]
    public void Accounts_are_monetary_by_type_unless_classified(AccountType type, ControlAccountType control, Ias29Classification classification, bool monetary)
    {
        Hyperinflation.IsMonetary(type, control, classification).Should().Be(monetary);
    }

    [Fact]
    public void Amounts_are_restated_from_the_month_they_were_recorded()
    {
        Dictionary<DateOnly, decimal> index = new() { [new DateOnly(2026, 1, 1)] = 100m, [new DateOnly(2026, 2, 1)] = 110m, [new DateOnly(2026, 3, 1)] = 121m };

        Hyperinflation.Restate(
            [new MonthlyMovement(new DateOnly(2026, 1, 1), 1_000m), new MonthlyMovement(new DateOnly(2026, 2, 1), -1_100m), new MonthlyMovement(new DateOnly(2026, 3, 1), 50m)],
            m => index[m], 121m).Should().Be(1_210m - 1_210m + 50m);
        Hyperinflation.Factor(121m, 100m).Should().Be(1.21m);
    }

    [Fact]
    public void A_non_positive_index_is_refused()
    {
        FluentActions.Invoking(() => Hyperinflation.Factor(121m, 0m)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
