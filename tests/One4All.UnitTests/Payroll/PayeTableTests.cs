using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

public class PayeTableTests
{
    private static readonly PayeBand[] Bands = [new(0m, 1000m, 0m), new(1000m, 3000m, 20m), new(3000m, null, 30m)];

    [Theory]
    [InlineData(800, 0)]
    [InlineData(1000, 0)]
    [InlineData(1900, 180)]
    [InlineData(3000, 400)]
    [InlineData(5000, 1000)]
    public void Each_band_taxes_its_slice(int taxable, int expected) => PayeTable.Tax(Bands, taxable).Should().Be(expected);

    [Fact]
    public void Band_order_does_not_matter_and_cents_round()
        => PayeTable.Tax([Bands[2], Bands[0], Bands[1]], 1000.05m).Should().Be(0.01m);
}
