using FluentAssertions;
using One4All.Core.Licensing;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

public class PermissionCatalogueTests
{
    [Fact]
    public void Codes_are_unique()
    {
        PermissionCatalogue.All.Select(p => p.Code)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_permission_targets_a_known_licence_module()
    {
        foreach (PermissionDefinition def in PermissionCatalogue.All)
        {
            LicenceModules.All.Should().Contain(def.LicenceModule, $"permission {def.Code}");
        }
    }

    [Fact]
    public void Code_is_module_dot_feature()
    {
        foreach (PermissionDefinition def in PermissionCatalogue.All)
        {
            def.Code.Should().Be($"{def.Module}.{def.Feature}");
        }
    }

    [Fact]
    public void Catalogue_covers_the_headline_modules()
    {
        string[] modules = PermissionCatalogue.All.Select(p => p.Module).Distinct().ToArray();
        modules.Should().Contain(["System", "Security", "GeneralLedger", "Housing", "Payroll", "Budget", "Municipal", "Pos"]);
    }

    [Fact]
    public void Housing_and_payroll_and_budget_permissions_exist()
    {
        PermissionCatalogue.Find("Housing.AllocationApprove").Should().NotBeNull();
        PermissionCatalogue.Find("Payroll.Commit").Should().NotBeNull();
        PermissionCatalogue.Find("Budget.Virement").Should().NotBeNull();
    }

    [Fact]
    public void ForLicenceModule_filters()
    {
        PermissionCatalogue.ForLicenceModule(LicenceModules.Housing)
            .Should().OnlyContain(p => p.Module == "Housing");
    }
}
