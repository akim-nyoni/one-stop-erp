using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the IFRS 9 provision matrix: banding by days past due and the rate per band.</summary>
public sealed class ExpectedCreditLossTests
{
    private static readonly EclMatrix Matrix = EclMatrix.Default; // current 1%, 1–30 3%, 31–60 10%, 61–90 25%, 90+ 50%

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 31)]
    [InlineData(90, 61)]
    [InlineData(91, 91)]
    [InlineData(400, 91)]
    public void Debts_fall_in_the_band_for_their_age(int daysPastDue, int bandStart)
    {
        Matrix.BandFor(daysPastDue).FromDaysPastDue.Should().Be(bandStart);
    }

    [Fact]
    public void Each_band_is_provided_at_its_rate()
    {
        IReadOnlyList<EclBandResult> bands = ExpectedCreditLoss.Compute(
            [new EclExposure(200m, -15), new EclExposure(400m, 10), new EclExposure(1_000m, 119), new EclExposure(0m, 45)],
            Matrix);

        bands.Select(b => (b.Exposure, b.Provision)).Should().Equal((200m, 2m), (400m, 12m), (0m, 0m), (0m, 0m), (1_000m, 500m));
        bands.Sum(b => b.Provision).Should().Be(514m);
    }
}
