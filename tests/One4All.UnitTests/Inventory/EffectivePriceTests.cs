using FluentAssertions;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

/// <summary>Covers date-effective prices: the standing price until a dated one takes over, per quantity break.</summary>
public sealed class EffectivePriceTests
{
    [Fact]
    public void Each_break_uses_the_latest_price_effective_on_the_day()
    {
        PriceList list = new(1, "RET", "Retail", CurrencyCode.Parse("USD"));
        list.SetPrice(7, 1m, 10m);
        list.SetPrice(7, 10m, 9m);
        list.SetPrice(7, 1m, 11m, new DateOnly(2026, 7, 1));
        list.SetPrice(7, 1m, 12m, new DateOnly(2027, 1, 1));
        list.SetPrice(7, 1m, 11.5m, new DateOnly(2026, 7, 1)); // re-scheduling the same day replaces it

        list.Resolve(7, 1m, new DateOnly(2026, 6, 30))!.UnitPrice.Should().Be(10m);
        list.Resolve(7, 1m, new DateOnly(2026, 7, 1))!.UnitPrice.Should().Be(11.5m);
        list.Resolve(7, 1m, new DateOnly(2027, 3, 1))!.UnitPrice.Should().Be(12m);
        list.Resolve(7, 12m, new DateOnly(2027, 3, 1))!.UnitPrice.Should().Be(9m, "the 10+ break has no dated price and keeps standing");
        list.Lines.Should().HaveCount(4);
        list.InForce(new DateOnly(2026, 8, 1)).Select(l => (l.MinimumQuantity, l.UnitPrice)).Should().BeEquivalentTo(new[] { (1m, 11.5m), (10m, 9m) });
    }
}
