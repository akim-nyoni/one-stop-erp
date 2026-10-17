using FluentAssertions;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

public class PermissionResolutionTests
{
    private const string Code = "Sales.Invoice";

    [Fact]
    public void Unions_operations_from_multiple_roles()
    {
        PermissionGrant[] grants =
        [
            new(Code, OperationFlags.View | OperationFlags.Create),
            new(Code, OperationFlags.Post),
        ];

        EffectivePermissions result = PermissionResolution.Resolve(grants, []);

        result.OperationsFor(Code).Should().Be(OperationFlags.View | OperationFlags.Create | OperationFlags.Post);
        result.Has(Code, OperationFlags.Post).Should().BeTrue();
    }

    [Fact]
    public void Grant_override_adds_operations()
    {
        PermissionGrant[] grants = [new(Code, OperationFlags.View)];
        PermissionOverride[] overrides = [new(Code, OperationFlags.Export, IsGrant: true)];

        EffectivePermissions result = PermissionResolution.Resolve(grants, overrides);

        result.Has(Code, OperationFlags.Export).Should().BeTrue();
    }

    [Fact]
    public void Deny_override_always_wins_even_over_a_grant_override()
    {
        PermissionGrant[] grants = [new(Code, OperationFlags.View | OperationFlags.Post)];
        PermissionOverride[] overrides =
        [
            new(Code, OperationFlags.Post, IsGrant: true),   // try to grant Post
            new(Code, OperationFlags.Post, IsGrant: false),  // deny Post - must win
        ];

        EffectivePermissions result = PermissionResolution.Resolve(grants, overrides);

        result.Has(Code, OperationFlags.Post).Should().BeFalse();
        result.Has(Code, OperationFlags.View).Should().BeTrue();
    }

    [Fact]
    public void Super_administrator_passes_everything()
    {
        EffectivePermissions admin = PermissionResolution.Resolve([], [], isSuperAdministrator: true);

        admin.Has("Anything.AtAll", OperationFlags.Post).Should().BeTrue();
        admin.HasAnyInModule("Housing").Should().BeTrue();
    }

    [Fact]
    public void HasAnyInModule_detects_module_access()
    {
        EffectivePermissions result = PermissionResolution.Resolve(
            [new("Housing.Stand", OperationFlags.View)], []);

        result.HasAnyInModule("Housing").Should().BeTrue();
        result.HasAnyInModule("Payroll").Should().BeFalse();
    }

    [Fact]
    public void Denying_the_only_operation_leaves_no_module_access()
    {
        EffectivePermissions result = PermissionResolution.Resolve(
            [new("Housing.Stand", OperationFlags.View)],
            [new("Housing.Stand", OperationFlags.View, IsGrant: false)]);

        result.HasAnyInModule("Housing").Should().BeFalse();
    }
}
