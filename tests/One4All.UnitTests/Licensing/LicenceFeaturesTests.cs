using FluentAssertions;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>Covers licence feature flags: edition defaults and what an enforced licence allows.</summary>
public sealed class LicenceFeaturesTests
{
    [Fact]
    public void Editions_carry_their_default_features()
    {
        LicenceFeatures.ForEdition(LicenceEdition.Standard).Should().Contain(LicenceFeatures.MultiCurrency).And.NotContain(LicenceFeatures.ApiAccess);
        LicenceFeatures.ForEdition(LicenceEdition.Professional).Should().Contain(LicenceFeatures.ApiAccess).And.NotContain(LicenceFeatures.Consolidation);
        LicenceFeatures.ForEdition(LicenceEdition.Enterprise).Should().Equal(LicenceFeatures.All);
    }

    [Fact]
    public void Only_an_enforced_licence_listing_features_restricts_them()
    {
        LicenceFeatures.Allows(false, ["MULTI_CURRENCY"], LicenceFeatures.Consolidation).Should().BeTrue();
        LicenceFeatures.Allows(true, null, LicenceFeatures.Consolidation).Should().BeTrue();
        LicenceFeatures.Allows(true, [], LicenceFeatures.Consolidation).Should().BeTrue("a licence from before feature flags keeps them all");
        LicenceFeatures.Allows(true, ["multi_currency"], LicenceFeatures.MultiCurrency).Should().BeTrue();
        LicenceFeatures.Allows(true, ["MULTI_CURRENCY"], LicenceFeatures.Consolidation).Should().BeFalse();
    }
}
