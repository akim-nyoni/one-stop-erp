using FluentAssertions;
using One4All.Application.Housing;
using Xunit;

namespace One4All.UnitTests.Housing;

public class WaitingListAgeingTests
{
    private static readonly DateOnly AsAt = new(2026, 6, 1);

    [Theory]
    [InlineData(2026, 1, 1, WaitingAgeBand.UnderOne)]
    [InlineData(2025, 6, 2, WaitingAgeBand.UnderOne)]
    [InlineData(2025, 6, 1, WaitingAgeBand.OneToThree)]
    [InlineData(2023, 6, 2, WaitingAgeBand.OneToThree)]
    [InlineData(2023, 6, 1, WaitingAgeBand.ThreeToFive)]
    [InlineData(2021, 6, 1, WaitingAgeBand.FiveToTen)]
    [InlineData(2016, 6, 2, WaitingAgeBand.FiveToTen)]
    [InlineData(2016, 6, 1, WaitingAgeBand.TenPlus)]
    public void Each_band_includes_its_lower_bound(int year, int month, int day, WaitingAgeBand band)
    {
        WaitingListAgeing.Band(new DateOnly(year, month, day), AsAt).Should().Be(band);
    }

    [Theory]
    [InlineData(0.0, "Under 300")]
    [InlineData(299.99, "Under 300")]
    [InlineData(300.0, "300 – 599")]
    [InlineData(999.99, "600 – 999")]
    [InlineData(1000.0, "1,000 and over")]
    public void Income_bands_include_their_lower_bound(double income, string band)
    {
        WaitingListAgeing.IncomeBand((decimal)income).Should().Be(band);
    }

    [Fact]
    public void Age_bands_use_completed_years()
    {
        WaitingListAgeing.AgeBand(new DateOnly(2000, 6, 2), AsAt).Should().Be("Under 26");
        WaitingListAgeing.AgeBand(new DateOnly(2000, 6, 1), AsAt).Should().Be("26 – 35");
        WaitingListAgeing.AgeBand(new DateOnly(1975, 6, 1), AsAt).Should().Be("51 and over");
        WaitingListAgeing.AgeBand(null, AsAt).Should().Be("Not recorded");
    }
}
