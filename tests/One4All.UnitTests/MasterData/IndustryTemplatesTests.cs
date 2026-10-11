using FluentAssertions;
using One4All.Application.MasterData;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.MasterData;

/// <summary>Covers the industry templates: Core always first, no duplicate codes across industries, and taxes and roles that resolve.</summary>
public sealed class IndustryTemplatesTests
{
    [Fact]
    public void Core_comes_first_and_each_industry_once()
        => IndustryTemplates.Plan([Vertical.School, Vertical.Retail, Vertical.School]).Select(t => t.Vertical).Should().Equal(Vertical.Core, Vertical.Retail, Vertical.School);

    [Fact]
    public void Account_codes_do_not_clash_and_fit_the_ledger()
    {
        List<string> codes = [.. IndustryTemplates.Plan(Enum.GetValues<Vertical>()).SelectMany(t => t.Accounts).Select(a => a.Code)];
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length <= 8);
    }

    [Fact]
    public void Every_industry_brings_demonstration_data_and_codes_do_not_clash()
    {
        IReadOnlyList<VerticalTemplate> all = IndustryTemplates.Plan(Enum.GetValues<Vertical>());
        all.Where(t => t.Vertical != Vertical.Core).Should().OnlyContain(t => t.DemoCustomers.Count + t.DemoSuppliers.Count + t.DemoItems.Count > 0);
        all.SelectMany(t => t.DemoCustomers).Select(c => c.Code).Should().OnlyHaveUniqueItems();
        all.SelectMany(t => t.DemoSuppliers).Select(c => c.Code).Should().OnlyHaveUniqueItems();
        all.SelectMany(t => t.DemoItems).Select(i => i.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Taxes_post_to_template_accounts_and_roles_grant_real_permissions()
    {
        IReadOnlyList<VerticalTemplate> all = IndustryTemplates.Plan(Enum.GetValues<Vertical>());
        HashSet<string> accounts = [.. all.SelectMany(t => t.Accounts).Select(a => a.Code)];
        all.SelectMany(t => t.TaxCodes).SelectMany(t => new[] { t.OutputAccount, t.InputAccount }).OfType<string>().Should().OnlyContain(code => accounts.Contains(code));
        all.SelectMany(t => t.DemoItems).SelectMany(i => new[] { i.RevenueAccount, i.CostAccount }).OfType<string>().Should().OnlyContain(code => accounts.Contains(code));
        all.SelectMany(t => t.Roles).SelectMany(r => r.Grants).Should().OnlyContain(g =>
            PermissionCatalogue.All.Any(p => string.Equals(p.Code, g.Scope, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Module, g.Scope, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Grant_levels_stay_within_what_a_permission_offers()
    {
        OperationFlags offered = OperationFlags.View | OperationFlags.Create | OperationFlags.Approve;
        IndustryTemplates.Operations(offered, GrantLevel.Read).Should().Be(OperationFlags.View);
        IndustryTemplates.Operations(offered, GrantLevel.Use).Should().Be(OperationFlags.View | OperationFlags.Create);
        IndustryTemplates.Operations(offered, GrantLevel.Full).Should().Be(offered);
    }
}
