using FluentAssertions;
using One4All.Application.Payroll;
using Xunit;

namespace One4All.UnitTests.Payroll;

/// <summary>Covers pay element lookup tables: bands to amounts or percentages, open-ended last bands, and refusing overlaps and bad text.</summary>
public sealed class PayLookupTableTests
{
    [Fact]
    public void Values_find_their_band()
    {
        IReadOnlyList<PayLookupBand> bands = PayLookupTable.Parse("1000-5000:2.5%; 0-1000:50; 5000-:150").Value;
        bands.Select(b => b.From).Should().Equal(0m, 1000m, 5000m);
        PayLookupTable.Amount(bands, 999.99m).Should().Be(50m);
        PayLookupTable.Amount(bands, 2000m).Should().Be(50m, "2.5% of 2000");
        PayLookupTable.Amount(bands, 5000m).Should().Be(150m);
        PayLookupTable.Amount(bands, -1m).Should().Be(0m);

        PayLookupTable.Parse("0-1000:50; 900-2000:60").Error.Code.Should().Be("Payroll.LookupOverlap");
        PayLookupTable.Parse("0-:50; 100-200:60").Error.Code.Should().Be("Payroll.LookupOverlap");
        PayLookupTable.Parse("cheap:50").Error.Code.Should().Be("Payroll.LookupBand");
        PayLookupTable.Parse("500-100:5").Error.Code.Should().Be("Payroll.LookupBand");
        PayLookupTable.Parse(null).Error.Code.Should().Be("Payroll.LookupEmpty");
    }
}
