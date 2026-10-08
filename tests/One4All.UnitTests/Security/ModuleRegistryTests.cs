using FluentAssertions;
using One4All.Application.Modules;
using One4All.Application.Security;
using One4All.Core.Licensing;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>
/// Covers the module registry: the built-in modules describe every permission module and are consistent; navigation shows only licensed
/// modules the user holds permissions in; operation groups, API areas and tasks resolve to their modules; a plug-in joins the registry
/// and is checked like the rest.
/// </summary>
public sealed class ModuleRegistryTests
{
    private static readonly EffectivePermissions BudgetClerk = new(new Dictionary<string, OperationFlags> { ["Budget.Preparation"] = OperationFlags.View | OperationFlags.Create });

    [Fact]
    public void The_built_in_modules_are_consistent()
    {
        ModuleRegistry registry = new();
        registry.Problems(["system.backup", "budget.reconcile-commitments", "reports.subscriptions"]).Should().BeEmpty();
        registry.Problems(["mystery.task"]).Should().ContainSingle(p => p.Contains("mystery.task"));
        registry.All.Select(m => m.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Navigation_follows_licence_and_permission()
    {
        ModuleRegistry registry = new();
        static bool Licensed(string code) => code is LicenceModules.Core or LicenceModules.Budget or LicenceModules.GeneralLedger;

        registry.Available(Licensed, BudgetClerk).Select(m => m.Code).Should().Equal("Budget");
        registry.Available(Licensed, new EffectivePermissions(new Dictionary<string, OperationFlags>(), isSuperAdministrator: true)).Select(m => m.Code)
            .Should().Equal("System", "Security", "GeneralLedger", "Budget", "Reporting");
        registry.Available(code => code == LicenceModules.Core, BudgetClerk).Should().BeEmpty("budgeting is not licensed");

        registry.IsMenuEntryAvailable("Budgeting", Licensed, BudgetClerk).Should().BeTrue();
        registry.IsMenuEntryAvailable("CRM", Licensed, BudgetClerk).Should().BeFalse();
        registry.IsMenuEntryAvailable("Sales & Schools", c => c is LicenceModules.School, new EffectivePermissions(new Dictionary<string, OperationFlags> { ["School.Student"] = OperationFlags.View }))
            .Should().BeTrue("a group shared by two modules shows when either is available");
        registry.IsMenuEntryAvailable("Nobody's group", _ => true, BudgetClerk).Should().BeFalse();
        registry.ForMenuEntry("Restaurant").Select(m => m.LicenceModule).Should().Equal([LicenceModules.PointOfSale], "the restaurant screens come with point of sale");

        registry.ForTask("housing.lapse-offers")!.LicenceModule.Should().Be(LicenceModules.Housing);
        registry.ForApiArea("trading").Select(m => m.Code).Should().BeEquivalentTo(["Sales", "Purchasing"]);
        ApiAccessRules.Requirement("GET", "/api/budgets/1")!.Modules.Should().Equal("Budget");
    }

    [Fact]
    public void A_plug_in_module_registers_itself()
    {
        ModuleDescriptor fleet = new("Fleet", "Fleet Management", LicenceModules.JobCosting, "🚚", "#333333", ["fleet"], ["fleet.service-due"], ["Fleet"]);
        ModuleRegistry registry = new([fleet]);
        registry.All.Should().EndWith(fleet);
        registry.ForTask("fleet.service-due").Should().Be(fleet);
        registry.ForMenuEntry("Fleet").Should().Equal(fleet);
        registry.Problems(["fleet.service-due"]).Should().BeEmpty();

        ModuleRegistry clash = new([fleet with { Code = "Budget", TaskKeys = ["budget.reconcile-commitments"], LicenceModule = "NOPE" }]);
        clash.Problems([]).Should().HaveCount(3);
    }
}
