using System.Text.RegularExpressions;
using FluentAssertions;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers mapping API routes to permissions, and that every API area in the server is mapped.</summary>
public sealed partial class ApiAccessRulesTests
{
    [Fact]
    public void Routes_map_to_modules_and_writes()
    {
        ApiAccessRules.Requirement("GET", "/api/gl/accounts/1000/activity").Should().BeEquivalentTo(new ApiRequirement(["GeneralLedger"], false));
        ApiAccessRules.Requirement("POST", "/api/housing/stands").Should().BeEquivalentTo(new ApiRequirement(["Housing"], true));
        ApiAccessRules.Requirement("POST", "/api/reports/GL1/run")!.IsWrite.Should().BeFalse();
        ApiAccessRules.Requirement("GET", "/api/ess/profile")!.Modules.Should().BeEmpty();
        ApiAccessRules.Requirement("GET", "/api/unknown/thing").Should().BeNull();
        ApiAccessRules.UserIdIsTarget("/api/municipal/faults/F-1/assign").Should().BeTrue();
    }

    [Fact]
    public void Reading_needs_any_grant_and_writing_a_changing_one()
    {
        EffectivePermissions viewer = new(new Dictionary<string, OperationFlags> { ["Sales.Customer"] = OperationFlags.View }, false);
        ApiAccessRules.Allows(viewer, new ApiRequirement(["Sales"], false)).Should().BeTrue();
        ApiAccessRules.Allows(viewer, new ApiRequirement(["Sales"], true)).Should().BeFalse();
        ApiAccessRules.Allows(viewer, new ApiRequirement(["GeneralLedger"], false)).Should().BeFalse();
        ApiAccessRules.Allows(new EffectivePermissions(new Dictionary<string, OperationFlags>(), true), new ApiRequirement(["GeneralLedger"], true)).Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", "/api/sop/orders", "SalesOrders", false)]
    [InlineData("GET", "/api/sop/orders/SO-1", "SalesOrders", false)]
    [InlineData("POST", "/api/sop/orders", "SalesOrders", true)]
    [InlineData("POST", "/api/sop/orders/SO-1/despatch", "SalesOrders", true)]
    [InlineData("POST", "/api/sop/orders/SO-1/invoice", "SalesOrders", true)]
    [InlineData("GET", "/api/pop/orders", "Procurement", false)]
    [InlineData("GET", "/api/pop/orders/PO-1", "Procurement", false)]
    [InlineData("POST", "/api/pop/orders", "Procurement", true)]
    [InlineData("POST", "/api/pop/orders/PO-1/receipts", "Procurement", true)]
    [InlineData("POST", "/api/pop/invoices/match", "Procurement", true)]
    [InlineData("GET", "/api/stock/on-hand", "Inventory", false)]
    [InlineData("GET", "/api/stock/valuation", "Inventory", false)]
    [InlineData("POST", "/api/stock/receipts", "Inventory", true)]
    [InlineData("POST", "/api/stock/issues", "Inventory", true)]
    [InlineData("POST", "/api/stock/transfers", "Inventory", true)]
    [InlineData("POST", "/api/stock/adjustments", "Inventory", true)]
    public void Order_and_stock_routes_need_their_module_to_read_and_a_changing_grant_to_write(string method, string path, string module, bool write)
    {
        ApiRequirement requirement = ApiAccessRules.Requirement(method, path)!;
        requirement.Modules.Should().Contain(module);
        requirement.IsWrite.Should().Be(write);
    }

    [Fact]
    public void Every_api_area_in_the_server_is_governed()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "One4All.Server", "Program.cs"))
            + File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "One4All.Server", "OrdersApiEndpoints.cs"));
        List<string> ungoverned = [.. Route().Matches(program).Select(m => m.Groups[1].Value).Distinct()
            .Where(path => !ApiAccessRules.AnonymousRoutes.Contains(path) && ApiAccessRules.Requirement("GET", path) is null)];
        ungoverned.Should().BeEmpty("every API area needs a permission module in ApiAccessRules");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "One4All.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("(?:app|api)\\.Map(?:Get|Post|Put|Delete)\\(\"(/api/[^\"]*)\"")]
    private static partial Regex Route();
}
