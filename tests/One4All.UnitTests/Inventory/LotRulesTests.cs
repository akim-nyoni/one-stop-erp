using FluentAssertions;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using Xunit;

namespace One4All.UnitTests.Inventory;

public sealed class LotRulesTests
{
    [Fact]
    public void Lots_must_account_for_the_movement()
    {
        LotRules.Validate(ItemTracking.None, 1m, [new LotQuantity("A", 1m)])!.Code.Should().Be("Inv.NotTracked");
        LotRules.Validate(ItemTracking.Batch, 5m, [])!.Code.Should().Be("Inv.LotLines");
        LotRules.Validate(ItemTracking.Batch, 5m, [new LotQuantity(" ", 5m)])!.Code.Should().Be("Inv.LotLines");
        LotRules.Validate(ItemTracking.Batch, 5m, [new LotQuantity("A", 3m)])!.Code.Should().Be("Inv.LotQuantityMismatch");
        LotRules.Validate(ItemTracking.Batch, 5m, [new LotQuantity("A", 3m), new LotQuantity("B", 2m)]).Should().BeNull();
        LotRules.Validate(ItemTracking.Serial, 2m, [new LotQuantity("S1", 2m)])!.Code.Should().Be("Inv.SerialQuantity");
        LotRules.Validate(ItemTracking.Serial, 2m, [new LotQuantity("S1", 1m), new LotQuantity("s1", 1m)])!.Code.Should().Be("Inv.DuplicateSerial");
    }

    [Fact]
    public void Expiry_defaults_from_shelf_life()
    {
        LotRules.Expiry(null, new DateOnly(2026, 1, 1), 30).Should().Be(new DateOnly(2026, 1, 31));
        LotRules.Expiry(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1), 30).Should().Be(new DateOnly(2026, 6, 1));
        LotRules.Expiry(null, new DateOnly(2026, 1, 1), null).Should().BeNull();
    }

    [Fact]
    public void Fefo_picks_earliest_expiry_first_and_skips_expired_stock()
    {
        LotOnHand[] onHand =
        [
            new("NOEXP", 10m, null, new DateOnly(2025, 1, 1)),
            new("LATE", 4m, new DateOnly(2026, 9, 1), new DateOnly(2026, 1, 1)),
            new("SOON", 3m, new DateOnly(2026, 4, 1), new DateOnly(2026, 2, 1)),
            new("GONE", 5m, new DateOnly(2026, 2, 1), new DateOnly(2025, 6, 1)),
        ];
        var (picks, shortfall) = LotRules.Fefo(onHand, 9m, new DateOnly(2026, 3, 1));
        picks.Select(p => (p.LotNumber, p.Quantity)).Should().Equal(("SOON", 3m), ("LATE", 4m), ("NOEXP", 2m));
        shortfall.Should().Be(0m);
        LotRules.Fefo(onHand, 20m, new DateOnly(2026, 3, 1)).Shortfall.Should().Be(3m);
    }

    [Theory]
    [InlineData(2026, 2, 28, "Expired")]
    [InlineData(2026, 3, 20, "Expiring")]
    [InlineData(2026, 3, 31, "Expiring")]
    [InlineData(2026, 4, 1, "OK")]
    public void Expiry_status_uses_the_window(int y, int m, int d, string expected)
        => LotRules.ExpiryStatus(new DateOnly(y, m, d), new DateOnly(2026, 3, 1), 30).Should().Be(expected);
}
