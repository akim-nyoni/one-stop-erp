using FluentAssertions;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

public class ScreenPermissionsTests
{
    private static IEnumerable<(string Module, string Title, bool IsDocument, bool IsEnquiry)> Screens() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Security", "console-screens.tsv"))
            .Where(l => !l.StartsWith('#') && l.Contains('\t'))
            .Select(l => l.Split('\t'))
            .Select(p => (p[0], p[1], p[2] == "True", p[3] == "Enquiries"));

    [Fact]
    public void Every_console_screen_is_gated_by_a_catalogue_feature()
    {
        foreach ((string module, string title, _, _) in Screens())
        {
            string? code = ScreenPermissions.CodeFor(module, title);
            code.Should().NotBeNull($"{module} / {title} needs a feature");
            PermissionCatalogue.Find(code!).Should().NotBeNull($"{module} / {title} maps to {code}");
        }
    }

    [Fact]
    public void What_a_screen_needs_is_always_something_the_grid_can_grant()
    {
        foreach ((string module, string title, bool isDocument, bool isEnquiry) in Screens())
        {
            PermissionDefinition feature = PermissionCatalogue.Find(ScreenPermissions.CodeFor(module, title)!)!;
            OperationFlags needs = ScreenPermissions.NeedsToRun(title, isDocument, isEnquiry, feature.Available);
            needs.Should().NotBe(OperationFlags.None);
            (needs & ~feature.Available).Should().Be(OperationFlags.None, $"{module} / {title} needs {needs} but {feature.Code} offers {feature.Available}");
        }
    }

    [Theory]
    [InlineData("System Manager", "Add a customer", "Sales.Customer")]
    [InlineData("Customers", "Customer statement", "Sales.Statement")]
    [InlineData("HR & Attendance", "Approve payroll", "Payroll.Approve")]
    [InlineData("HR & Attendance", "Take leave", "Payroll.Leave")]
    [InlineData("Receipting", "Take a payment", "CashBook.Teller")]
    [InlineData("Housing", "Allocation run", "Housing.AllocationRun")]
    [InlineData("Housing", "Allocation appeals", "Housing.AllocationApprove")]
    [InlineData("Documents", "Quotation", "Reporting.Report")]
    [InlineData("Stock", "Post a stocktake", "Inventory.StockTake")]
    [InlineData("Approvals", "Segregation-of-duties rules", "Security.SodMatrix")]
    public void Screens_map_to_the_expected_feature(string module, string title, string expected)
        => ScreenPermissions.CodeFor(module, title).Should().Be(expected);

    [Fact]
    public void Unknown_console_module_is_open_to_anyone()
    {
        ScreenPermissions.CodeFor("Workbench", "Anything").Should().BeNull();
        ScreenPermissions.Refusal(EffectivePermissions.None, null, OperationFlags.Post, "Anything").Should().BeNull();
    }

    [Fact]
    public void Approving_reversing_printing_and_enquiring_need_their_own_flags()
    {
        ScreenPermissions.NeedsToRun("Approve payroll", false, false, PermissionCatalogue.Find("Payroll.Approve")!.Available).Should().Be(OperationFlags.Approve | PermissionDefinition.AllAuthoriseLevels & PermissionCatalogue.Find("Payroll.Approve")!.Available);
        ScreenPermissions.NeedsToRun("Reverse a receipt", false, false, PermissionDefinition.Transaction).Should().Be(OperationFlags.VoidReverse | OperationFlags.Post);
        ScreenPermissions.NeedsToRun("Quotation", true, false, PermissionDefinition.ReadOnly | OperationFlags.Reprint).Should().Be(OperationFlags.Print | OperationFlags.Reprint);
        ScreenPermissions.NeedsToRun("Property enquiry", false, true, PermissionDefinition.MasterData).Should().Be(OperationFlags.View);
        ScreenPermissions.NeedsToRun("Add a customer", false, false, PermissionDefinition.MasterData).Should().Be(OperationFlags.Create | OperationFlags.Edit | OperationFlags.Delete);
    }

    [Fact]
    public void Refusal_names_the_feature_and_is_lifted_by_any_needed_flag()
    {
        EffectivePermissions viewOnly = new(new Dictionary<string, OperationFlags>(StringComparer.OrdinalIgnoreCase) { ["Sales.Customer"] = OperationFlags.View });
        string? refused = ScreenPermissions.Refusal(viewOnly, "Sales.Customer", OperationFlags.Create | OperationFlags.Edit, "Add a customer");
        refused.Should().Contain("Customers").And.Contain("Sales.Customer");
        ScreenPermissions.Refusal(viewOnly, "Sales.Customer", OperationFlags.View, "Customers").Should().BeNull();
        ScreenPermissions.CanSee(viewOnly, "Sales.Customer").Should().BeTrue();
        ScreenPermissions.CanSee(viewOnly, "Sales.Invoice").Should().BeFalse();
        ScreenPermissions.CanSee(new EffectivePermissions(new Dictionary<string, OperationFlags>(), isSuperAdministrator: true), "Sales.Invoice").Should().BeTrue();
    }
}
