using FluentAssertions;
using One4All.Application.MasterData;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.MasterData;

/// <summary>Covers the embedded seed charts of accounts: every industry has one, codes are unique across a full setup, and the templates' own accounts keep their control types.</summary>
public sealed class ChartOfAccountsTests
{
    [Fact]
    public void Every_industry_has_a_seed_chart_that_merges_cleanly()
    {
        foreach (Vertical vertical in Enum.GetValues<Vertical>())
        {
            ChartOfAccounts.Load(vertical).Should().NotBeEmpty($"{vertical} needs docs/coa/{vertical.ToString().ToLowerInvariant()}.csv");
        }

        IReadOnlyList<AccountSeed> all = [.. IndustryTemplates.Plan(Enum.GetValues<Vertical>()).SelectMany(t => t.Accounts)];
        all.Select(a => a.Code).Should().OnlyHaveUniqueItems("a full setup creates every account once");
        all.Should().Contain(a => a.Code == "1200" && a.Control == ControlAccountType.AccountsReceivable && a.ReportCategory == "Receivables");
        all.Should().Contain(a => a.Code == "6000" && a.BudgetEnabled);
        all.Should().Contain(a => a.Code == "1600" && a.Name == "Property, Plant & Equipment", "quoted names keep their commas");
        all.Count.Should().BeGreaterThan(200);
    }

    [Fact]
    public void A_chart_with_an_unknown_type_is_refused()
    {
        FluentActions.Invoking(() => ChartOfAccounts.Parse("code,name,type\n9999,Odd,Mystery\n")).Should().Throw<FormatException>().WithMessage("*Mystery*");
        ChartOfAccounts.Parse("code,name,type,control,category,budget\n1234,Cash,Asset,,Cash and Bank,yes\n").Single()
            .Should().Be(new AccountSeed("1234", "Cash", AccountType.Asset, null, "Cash and Bank", true));
    }
}
