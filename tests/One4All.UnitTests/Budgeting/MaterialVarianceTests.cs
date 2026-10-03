using FluentAssertions;
using One4All.Application.Budgeting;
using Xunit;

namespace One4All.UnitTests.Budgeting;

/// <summary>Covers which budget variances are material: by value, by percentage, either way.</summary>
public sealed class MaterialVarianceTests
{
    private static readonly VarianceThreshold Threshold = new(1_000m, 10m);

    [Theory]
    [InlineData(10_000, 11_000, true)]   // 1,000 over: at the value threshold
    [InlineData(10_000, 9_100, false)]   // 900 under and 9%: neither
    [InlineData(5_000, 5_500, true)]     // 500 over but 10%: at the percentage
    [InlineData(5_000, 4_400, true)]     // underspend counts too
    [InlineData(5_000, 5_000, false)]    // no variance
    [InlineData(0, 50, true)]            // spending with no budget at all
    public void Variances_are_material_by_value_or_percentage(int budget, int actual, bool material)
    {
        MaterialVariance.IsMaterial(budget, actual, Threshold).Should().Be(material);
    }
}
