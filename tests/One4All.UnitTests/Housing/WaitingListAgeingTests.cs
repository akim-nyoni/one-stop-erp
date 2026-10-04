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
}
