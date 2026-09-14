using FluentAssertions;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

public class SegregationOfDutiesTests
{
    [Fact]
    public void Detects_prepare_and_approve_budget_conflict()
    {
        EffectivePermissions permissions = PermissionResolution.Resolve(
        [
            new("Budget.Prepare", OperationFlags.Create),
            new("Budget.Approve", OperationFlags.Approve),
        ], []);

        SegregationOfDutiesCatalogue.Violations(permissions)
            .Select(r => r.Code).Should().Contain("SOD-BUD-01");
    }

    [Fact]
    public void No_conflict_when_only_one_side_held()
    {
        EffectivePermissions permissions = PermissionResolution.Resolve(
            [new("Payroll.Calculate", OperationFlags.Create)], []);

        SegregationOfDutiesCatalogue.Violations(permissions).Should().BeEmpty();
    }

    [Fact]
    public void Detects_payroll_process_and_approve_conflict()
    {
        EffectivePermissions permissions = PermissionResolution.Resolve(
        [
            new("Payroll.Calculate", OperationFlags.Create),
            new("Payroll.Approve", OperationFlags.Approve),
        ], []);

        SegregationOfDutiesCatalogue.Violations(permissions)
            .Should().ContainSingle(r => r.Code == "SOD-PAY-01");
    }

    [Fact]
    public void Caveat_place_and_lift_is_a_conflict()
    {
        EffectivePermissions permissions = PermissionResolution.Resolve(
            [new("Housing.Caveat", OperationFlags.Create | OperationFlags.Delete)], []);

        SegregationOfDutiesCatalogue.Violations(permissions)
            .Should().Contain(r => r.Code == "SOD-HSE-04");
    }

    [Fact]
    public void Every_catalogue_rule_references_known_permissions()
    {
        foreach (SodRule rule in SegregationOfDutiesCatalogue.All)
        {
            PermissionCatalogue.Find(rule.PermissionA).Should().NotBeNull($"{rule.Code} side A '{rule.PermissionA}'");
            PermissionCatalogue.Find(rule.PermissionB).Should().NotBeNull($"{rule.Code} side B '{rule.PermissionB}'");
        }
    }
}
