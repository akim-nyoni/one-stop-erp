using FluentAssertions;
using One4All.Core.Security;
using Xunit;

namespace One4All.UnitTests.Security;

/// <summary>Covers the SoD rules in force: built-ins re-tuned or switched off by code, and new rules added.</summary>
public sealed class SodRulesTableTests
{
    [Fact]
    public void Configured_rules_replace_switch_off_or_add_to_the_built_ins()
    {
        SodRule retuned = SegregationOfDutiesCatalogue.All.Single(r => r.Code == "SOD-AP-01") with { Severity = SodSeverity.Medium };
        SodRule added = new("SOD-INV-01", "Adjust stock and approve stocktake", "Inventory.Adjustment", OperationFlags.Create, "Inventory.Stocktake", OperationFlags.Approve, SodSeverity.High);
        SodRule off = SegregationOfDutiesCatalogue.All.Single(r => r.Code == "SOD-HSE-04");

        IReadOnlyList<SodRule> effective = SegregationOfDutiesCatalogue.Effective([(retuned, true), (added, true), (off, false)]);
        effective.Should().HaveCount(SegregationOfDutiesCatalogue.All.Count);
        effective.Single(r => r.Code == "SOD-AP-01").Severity.Should().Be(SodSeverity.Medium);
        effective.Should().Contain(added).And.NotContain(r => r.Code == "SOD-HSE-04");
        SegregationOfDutiesCatalogue.Effective([]).Should().BeEquivalentTo(SegregationOfDutiesCatalogue.All);
    }
}
