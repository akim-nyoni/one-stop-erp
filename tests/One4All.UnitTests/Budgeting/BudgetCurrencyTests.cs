using FluentAssertions;
using One4All.Core.Budgeting;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers a budget's header currency: base equivalents follow the budget rate; other currencies and driver lines are left alone.</summary>
public sealed class BudgetCurrencyTests
{
    [Fact]
    public void Base_equivalents_follow_the_budget_rate()
    {
        Budget budget = new(1, 1, "Plan", BudgetType.OriginalApproved, null, 1);
        BudgetLine plain = budget.AddLine("6000", 1, 1000m, null);
        BudgetLine euro = budget.AddLine("6100", 1, 500m, null);
        euro.Currency = CurrencyCode.Parse("EUR");
        euro.PlannedRate = 1.1m;
        BudgetLine driven = budget.AddLine("4000", 1, 400m, null);
        driven.DriverCode = "FEE";

        CurrencyCode zar = CurrencyCode.Parse("ZAR");
        budget.SetCurrency(zar, 0.05m);
        (plain.Currency, plain.PlannedRate, plain.Amount).Should().Be((zar, 0.05m, 1000m), "20,000 rand at 0.05");

        budget.SetCurrency(zar, 0.055m);
        plain.Amount.Should().Be(1100m, "the same 20,000 rand at the new rate");
        euro.Amount.Should().Be(500m);
        driven.Amount.Should().Be(400m);
        driven.Currency.Should().BeNull();

        budget.SetCurrency(null, null);
        (plain.Currency, plain.PlannedRate, plain.Amount, budget.Currency, budget.BudgetRate).Should().Be(((CurrencyCode?)null, (decimal?)null, 1100m, (CurrencyCode?)null, (decimal?)null));
        euro.Currency.Should().Be(CurrencyCode.Parse("EUR"));
    }
}
