using FluentAssertions;
using One4All.Application.Security;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers the desktop console's permission and licence check: reading needs any grant in the module, running an action one that changes data.</summary>
public sealed class DesktopAccessRulesTests
{
    private static readonly Func<string, bool> AllLicensed = _ => true;

    [Fact]
    public void Running_an_action_needs_a_changing_grant_in_the_module()
    {
        EffectivePermissions viewer = new(new Dictionary<string, OperationFlags> { ["FixedAssets.Asset"] = OperationFlags.View }, false);
        DesktopAccessRules.Refusal(viewer, "Fixed Assets", false, AllLicensed).Should().BeNull();
        DesktopAccessRules.Refusal(viewer, "Fixed Assets", true, AllLicensed).Should().Contain("no permission to change data");
        DesktopAccessRules.Refusal(viewer, "Banking", false, AllLicensed).Should().Contain("no permission");

        EffectivePermissions poster = new(new Dictionary<string, OperationFlags> { ["FixedAssets.Depreciation"] = OperationFlags.View | OperationFlags.Post }, false);
        DesktopAccessRules.Refusal(poster, "Fixed Assets", true, AllLicensed).Should().BeNull();
        DesktopAccessRules.Refusal(new EffectivePermissions(new Dictionary<string, OperationFlags>(), true), "Banking", true, AllLicensed).Should().BeNull();
    }

    [Fact]
    public void An_unlicensed_module_is_refused_and_an_unclaimed_one_is_open()
    {
        EffectivePermissions admin = new(new Dictionary<string, OperationFlags>(), true);
        DesktopAccessRules.Refusal(admin, "Fixed Assets", false, _ => false).Should().Contain("not licensed");
        DesktopAccessRules.ModulesFor("No Such Console").Should().BeEmpty();
        DesktopAccessRules.Refusal(admin, "No Such Console", true, _ => false).Should().BeNull();
    }
}
