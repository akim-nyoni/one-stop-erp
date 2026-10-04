using FluentAssertions;
using One4All.Application.Municipal;
using One4All.Core.Municipal;
using Xunit;

namespace One4All.UnitTests.Municipal;

public class TariffCatalogueTests
{
    private static readonly DateOnly Date = new(2026, 6, 1);

    private static readonly TariffCandidate[] Candidates =
    [
        new(1, "DOM", ConsumerCategory.Domestic, null, new DateOnly(2026, 1, 1)),
        new(2, "DOM-2027", ConsumerCategory.Domestic, null, new DateOnly(2027, 1, 1)),
        new(3, "COM", ConsumerCategory.Commercial, null, new DateOnly(2026, 1, 1)),
        new(4, "DOM-W3", ConsumerCategory.Domestic, "Ward 3", new DateOnly(2026, 1, 1)),
    ];

    [Fact]
    public void A_ward_tariff_beats_a_council_wide_one()
    {
        TariffCatalogue.Best(Candidates, ConsumerCategory.Domestic, "Ward 3", Date)!.Value.TariffId.Should().Be(4);
    }

    [Fact]
    public void Without_a_ward_tariff_the_council_wide_one_applies()
    {
        TariffCatalogue.Best(Candidates, ConsumerCategory.Domestic, "Ward 9", Date)!.Value.TariffId.Should().Be(1);
    }

    [Fact]
    public void The_category_must_match_exactly()
    {
        TariffCatalogue.Best(Candidates, ConsumerCategory.Commercial, "Ward 3", Date)!.Value.TariffId.Should().Be(3);
        TariffCatalogue.Best(Candidates, ConsumerCategory.Industrial, "Ward 3", Date).Should().BeNull();
    }

    [Fact]
    public void The_latest_version_in_force_wins()
    {
        TariffCatalogue.Best(Candidates, ConsumerCategory.Domestic, null, new DateOnly(2027, 6, 1))!.Value.TariffId.Should().Be(2);
        TariffCatalogue.Best(Candidates, ConsumerCategory.Domestic, null, new DateOnly(2025, 6, 1)).Should().BeNull();
    }
}
