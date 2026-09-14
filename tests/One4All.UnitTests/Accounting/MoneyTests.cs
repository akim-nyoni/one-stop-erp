using FluentAssertions;
using One4All.Core.Accounting;
using Xunit;

namespace One4All.UnitTests.Accounting;

public class MoneyTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.Parse("USD");
    private static readonly CurrencyCode Zwl = CurrencyCode.Parse("ZWL");

    [Fact]
    public void Constructor_rounds_to_storage_scale_using_bankers_rounding()
    {
        new Money(1.23455m, Usd).Amount.Should().Be(1.2346m); // .23455 -> even .2346
        new Money(1.23445m, Usd).Amount.Should().Be(1.2344m); // .23445 -> even .2344
    }

    [Fact]
    public void Add_and_subtract_same_currency()
    {
        (new Money(10.50m, Usd) + new Money(4.25m, Usd)).Should().Be(new Money(14.75m, Usd));
        (new Money(10.00m, Usd) - new Money(4.25m, Usd)).Should().Be(new Money(5.75m, Usd));
    }

    [Fact]
    public void Operating_across_currencies_throws()
    {
        Action act = () => _ = new Money(10m, Usd) + new Money(10m, Zwl);
        act.Should().Throw<InvalidOperationException>().WithMessage("*different currencies*");
    }

    [Fact]
    public void Multiply_scales_amount()
    {
        (new Money(2.50m, Usd) * 3m).Should().Be(new Money(7.50m, Usd));
    }

    [Fact]
    public void Allocate_splits_without_losing_a_cent()
    {
        Money[] parts = new Money(10.00m, Usd).Allocate(3);

        // Allocation is lossless at Money's 4-dp storage scale: 10.0000 / 3.
        parts.Should().HaveCount(3);
        parts.Select(p => p.Amount).Should().Equal(3.3334m, 3.3333m, 3.3333m);
        parts.Aggregate(Money.Zero(Usd), (acc, p) => acc + p).Should().Be(new Money(10.00m, Usd));
    }

    [Fact]
    public void Allocate_distributes_negative_remainder()
    {
        Money[] parts = new Money(-0.10m, Usd).Allocate(3);
        parts.Aggregate(Money.Zero(Usd), (acc, p) => acc + p).Should().Be(new Money(-0.10m, Usd));
    }

    [Fact]
    public void Comparisons_respect_amount()
    {
        (new Money(5m, Usd) > new Money(4.9999m, Usd)).Should().BeTrue();
        (new Money(5m, Usd) <= new Money(5m, Usd)).Should().BeTrue();
    }

    [Fact]
    public void Zero_and_sign_helpers()
    {
        Money.Zero(Usd).IsZero.Should().BeTrue();
        new Money(-1m, Usd).IsNegative.Should().BeTrue();
        new Money(1m, Usd).IsPositive.Should().BeTrue();
    }
}
