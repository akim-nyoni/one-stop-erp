using FluentAssertions;
using One4All.Application.Housing;
using One4All.Core.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class AcquisitionReadinessTests
{
    private static readonly SignedOffItem[] AllAcceptable = [.. Enum.GetValues<DueDiligenceItem>().Select(i => new SignedOffItem(i, true))];
    private static readonly CostElement[] Price = [new(AcquisitionCostComponent.PurchasePrice, 1000m)];

    [Fact]
    public void A_signed_off_valued_purchase_with_a_price_is_ready()
    {
        AcquisitionReadiness.Problems(AcquisitionMethod.Purchase, AllAcceptable, valued: true, Price).Should().BeEmpty();
    }

    [Fact]
    public void Missing_and_unacceptable_items_are_named()
    {
        SignedOffItem[] partial = [new(DueDiligenceItem.TitleSearch, true), new(DueDiligenceItem.Wetland, false)];
        IReadOnlyList<string> problems = AcquisitionReadiness.Problems(AcquisitionMethod.Donation, partial, valued: false, []);
        problems.Should().HaveCount(2);
        problems[0].Should().StartWith("Not signed off: Encumbrances, Caveats").And.NotContain("TitleSearch");
        problems[1].Should().Be("Not acceptable: Wetland.");
    }

    [Theory]
    [InlineData(AcquisitionMethod.Purchase, true)]
    [InlineData(AcquisitionMethod.Compulsory, true)]
    [InlineData(AcquisitionMethod.LandSwap, true)]
    [InlineData(AcquisitionMethod.Donation, false)]
    [InlineData(AcquisitionMethod.StateGrant, false)]
    public void A_valuation_is_needed_where_value_changes_hands(AcquisitionMethod method, bool needed)
    {
        AcquisitionReadiness.Problems(method, AllAcceptable, valued: false, Price).Contains("No valuation obtained.").Should().Be(needed);
    }

    [Fact]
    public void A_purchase_needs_its_price()
    {
        AcquisitionReadiness.Problems(AcquisitionMethod.Purchase, AllAcceptable, valued: true, [new(AcquisitionCostComponent.Legal, 10m)]).Should().Equal("No purchase price recorded.");
    }

    [Fact]
    public void The_build_up_totals_each_element_in_order()
    {
        CostElement[] costs = [new(AcquisitionCostComponent.Legal, 300m), new(AcquisitionCostComponent.PurchasePrice, 1000m), new(AcquisitionCostComponent.Legal, 200m)];
        AcquisitionReadiness.BuildUp(costs).Should().Equal(new CostElement(AcquisitionCostComponent.PurchasePrice, 1000m), new CostElement(AcquisitionCostComponent.Legal, 500m));
    }
}
